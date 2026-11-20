using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AllyRemoteBridge.Devices;

// Invoked only by the installer after explicit user approval. Never adds certificates.
public static class DriverInstaller
{
    public static void InstallReviewedPackage()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Driver installation requires an elevated installer.");
        var integrity = new CodeIntegrity { Length = 8 };
        int status = NtQuerySystemInformation(103, ref integrity, 8, out _);
        if (status != 0 || (integrity.Options & 1) == 0 || (integrity.Options & 2) != 0)
            throw new InvalidOperationException("Windows code integrity must be enabled and test signing must be disabled.");
        string directory = Path.Combine(AppContext.BaseDirectory, "driver");
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "approved-driver.json")));
        var root = policy.RootElement;
        if (!root.GetProperty("reviewed").GetBoolean() || root.GetProperty("sourceRevision").GetString() != "77a781eaedc7f6b6cb171a92d378dee408ad7408")
            throw new InvalidDataException("No audited driver package is approved for this build.");
        foreach (string name in new[] { "usbip2_ude.inf", "usbip2_ude.cat", "usbip2_ude.sys" })
        {
            using var file = File.OpenRead(Path.Combine(directory, name));
            string hash = Convert.ToHexString(SHA256.HashData(file));
            if (!hash.Equals(root.GetProperty("files").GetProperty(name).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Driver integrity check failed: {name}");
        }
        string thumbprint = root.GetProperty("catalogSignerThumbprint").GetString() ?? "";
        if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit)) throw new InvalidDataException("Driver signer pin is missing.");
        Authenticode.VerifyTrusted(Path.Combine(directory, "usbip2_ude.cat"), thumbprint);
        // Register only the virtual host controller described by the audited INF.
        var classGuid = new Guid("36FC9E60-C465-11CF-8056-444553540000");
        nint set = SetupDiCreateDeviceInfoList(ref classGuid, 0);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        bool registered = false, success = false;
        var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
        try
        {
            // If an existing controller is present, update through Windows rather than creating a duplicate.
            bool existing = HasController();
            if (!existing)
            {
                if (!SetupDiCreateDeviceInfo(set, "USBIP_WIN2_UDE", ref classGuid, "Ally Remote Bridge USB/IP host", 0, 1, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] hardware = Encoding.Unicode.GetBytes("ROOT\\USBIP_WIN2\\UDE\0\0");
                if (!SetupDiSetDeviceRegistryProperty(set, ref info, 1, hardware, (uint)hardware.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!SetupDiCallClassInstaller(0x19, set, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                registered = true;
            }
            if (!UpdateDriverForPlugAndPlayDevices(0, @"ROOT\USBIP_WIN2\UDE", Path.Combine(directory, "usbip2_ude.inf"), 0, out bool reboot))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the driver package.");
            success = true;
            if (reboot) File.WriteAllText(Path.Combine(directory, "restart-required"), "Restart Windows before using the bridge.");
        }
        finally
        {
            if (registered && !success) SetupDiCallClassInstaller(5, set, ref info); // Roll back only our new root device.
            SetupDiDestroyDeviceInfoList(set);
        }
    }
    private static bool HasController()
    {
        var guid = new Guid("36FC9E60-C465-11CF-8056-444553540000");
        nint set = SetupDiGetClassDevs(ref guid, null, 0, 2);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 259) throw new Win32Exception(error);
                    return false;
                }
                byte[] buffer = new byte[4096];
                if (SetupDiGetDeviceRegistryProperty(set, ref info, 1, out _, buffer, (uint)buffer.Length, out _)
                    && Encoding.Unicode.GetString(buffer).Split('\0').Any(s => s.Equals(@"ROOT\USBIP_WIN2\UDE", StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CodeIntegrity { public uint Length, Options; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public uint Size; public Guid Class; public uint DevInst; public nuint Reserved; }
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int kind, ref CodeIntegrity information, uint length, out uint returned);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern nint SetupDiCreateDeviceInfoList(ref Guid guid, nint parent);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiCreateDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCreateDeviceInfo(nint set, string name, ref Guid guid, string description, nint parent, uint flags, ref DeviceInfo info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiSetDeviceRegistryProperty(nint set, ref DeviceInfo info, uint property, byte[] value, uint size);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryProperty(nint set, ref DeviceInfo info, uint property, out uint type, byte[] value, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCallClassInstaller(uint function, nint set, ref DeviceInfo info);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateDriverForPlugAndPlayDevices(nint parent, string hardware, string inf, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);
}
