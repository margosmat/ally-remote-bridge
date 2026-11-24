using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllyRemoteBridge.Devices;

internal static class HidDiscovery
{
    internal static bool HasDS4(string serial, uint controllerNode, int? port)
    {
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (set == -1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate HID devices.");
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 259) throw new System.ComponentModel.Win32Exception(error, "HID enumeration failed.");
                    break;
                }
                var deviceInfo = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                SetupDiGetDeviceInterfaceDetail(set, ref info, 0, 0, out uint size, ref deviceInfo);
                if (size == 0) throw new IOException("Cannot determine HID interface path size.");
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, 8);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, ref deviceInfo)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    if (!path.Contains("vid_054c&pid_09cc", StringComparison.OrdinalIgnoreCase)) continue;
                    // The USB/IP serial can be available on the parent even when HID exposes none.
                    if (MatchesUsbDevice(deviceInfo.Node, serial, controllerNode, port)) return true;
                    using var file = CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (file.IsInvalid) continue;
                    var attrs = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(file, ref attrs)) continue;
                    if (attrs.Vendor != 0x054c || attrs.Product != 0x09cc) continue;
                    byte[] buffer = new byte[256];
                    if (HidD_GetSerialNumberString(file, buffer, buffer.Length))
                    {
                        string actual = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                        // The driver can expose its 15-character override or the 16-digit DS4 metadata.
                        if (MatchesSerial(actual, serial)) return true;
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return false;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    private static bool MatchesSerial(string value, string serial) =>
        value.Equals(serial, StringComparison.OrdinalIgnoreCase) ||
        value.Equals(serial.PadLeft(16, '0'), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesUsbDevice(uint node, string serial, uint controllerNode, int? port)
    {
        const string prefix = @"USB\VID_054C&PID_09CC\";
        var id = new System.Text.StringBuilder(200);
        do
        {
            if (CM_Get_Device_ID(node, id, id.Capacity, 0) != 0) return false;
            string value = id.ToString();
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (MatchesSerial(value[prefix.Length..], serial)) return true;
                if (port is not > 0) return false;

                // USB device address on this root hub is the port selected by the VHCI driver.
                const uint addressProperty = 0x1D; // CM_DRP_ADDRESS
                uint length = sizeof(uint);
                if (CM_Get_DevNode_Registry_Property(node, addressProperty, out uint type, out uint address, ref length, 0) != 0 ||
                    type != 4 || length != sizeof(uint) || address != (uint)port.Value) return false;

                while (CM_Get_Parent(out node, node, 0) == 0)
                    if (node == controllerNode) return true;
                return false;
            }
        } while (CM_Get_Parent(out node, node, 0) == 0);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint Node; public nuint Reserved; }
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID(uint node, System.Text.StringBuilder id, int length, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Registry_PropertyW", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_Property(uint node, uint property, out uint type, out uint value, ref uint length, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public uint Size; public Guid Guid; public uint Flags; public nuint Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, ref DeviceInfo device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
