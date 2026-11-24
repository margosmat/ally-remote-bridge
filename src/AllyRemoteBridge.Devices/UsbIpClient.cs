using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllyRemoteBridge.Devices;

// Contract: usbip-win2 header revision 77a781eaedc7f6b6cb171a92d378dee408ad7408.
// Unsupported layouts fail closed; no shell fallback and no detach-all operation.
internal sealed class UsbIpClient : IDisposable
{
    private readonly SafeFileHandle handle;
    internal uint ControllerNode { get; }
    private static readonly Guid InterfaceId = new("B4030C06-DC5F-4FCC-87EB-E5515A0935C0");
    internal UsbIpClient()
    {
        var guid = InterfaceId;
        nint set = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var info = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, 0, ref info)) throw new InvalidOperationException("usbip-win2 driver is unavailable. Install a reviewed, signed driver package first.");
            SetupDiGetDeviceInterfaceDetail(set, ref info, 0, 0, out uint size, 0);
            nint data = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.WriteInt32(data, 8); // Windows x64 SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                var device = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiGetDeviceInterfaceDetailWithInfo(set, ref info, data, size, out _, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
                ControllerNode = device.Node;
                string path = Marshal.PtrToStringUni(data + 4)!;
                handle = CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access USB/IP driver as current user.");
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    internal int Attach(string bus, string serial)
    {
        byte[] request = UsbIpProtocol.Attach(bus, serial);
        // PLUGIN_HARDWARE_ONCE prevents reconnect attempts after bridge termination.
        Invoke(0x806, request, request);
        int port = BitConverter.ToInt32(request, 4);
        if (port <= 0) throw new InvalidDataException("USB/IP driver returned an invalid port.");
        return port;
    }
    internal bool DetachOwned(string bus, string serial, int? expectedPort)
    {
        byte[] query = new byte[4 + UsbIpProtocol.ImportedSize * 256];
        BitConverter.GetBytes(UsbIpProtocol.QueryHeaderSize).CopyTo(query, 0);
        uint count = Invoke(0x802, query, query);
        var ports = UsbIpProtocol.OwnedPorts(query, count, bus, serial, expectedPort);
        foreach (int port in ports) Invoke(0x801, UsbIpProtocol.Detach(port), []);
        return ports.Count != 0;
    }
    private uint Invoke(uint function, byte[] input, byte[] output)
    {
        uint code = (0x22u << 16) | (3u << 14) | (function << 2);
        if (!DeviceIoControl(handle, code, input, (uint)input.Length, output, (uint)output.Length, out uint returned, 0))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"USB/IP operation 0x{function:X3} failed (Win32 {error}): {new Win32Exception(error).Message}");
        }
        return returned;
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint Node; public nuint Reserved; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailWithInfo(nint set, ref InterfaceData detail, nint data, uint size, out uint required, ref DeviceInfo device);
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public uint Size; public Guid Guid; public uint Flags; public nuint Reserved; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint returned, nint overlapped);
}
