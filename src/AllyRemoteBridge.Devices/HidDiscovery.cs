using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllyRemoteBridge.Devices;

internal static class HidDiscovery
{
    internal static bool HasDS4(string serial)
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
                SetupDiGetDeviceInterfaceDetail(set, ref info, 0, 0, out uint size, 0);
                if (size == 0) throw new IOException("Cannot determine HID interface path size.");
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, 8);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    if (!path.Contains("vid_054c&pid_09cc", StringComparison.OrdinalIgnoreCase)) continue;
                    using var file = CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (file.IsInvalid) throw new IOException("Cannot inspect a DS4 HID interface; device removal cannot be confirmed.");
                    var attrs = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(file, ref attrs)) throw new IOException("Cannot read DS4 HID attributes.");
                    if (attrs.Vendor != 0x054c || attrs.Product != 0x09cc) continue;
                    byte[] buffer = new byte[256];
                    if (!HidD_GetSerialNumberString(file, buffer, buffer.Length)) throw new IOException("Cannot read DS4 HID serial; device removal cannot be confirmed.");
                    {
                        string actual = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                        // The driver can expose its 15-character override or the 16-digit DS4 metadata.
                        if (actual == serial || actual == serial.PadLeft(16, '0')) return true;
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            return false;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public uint Size; public Guid Guid; public uint Flags; public nuint Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
