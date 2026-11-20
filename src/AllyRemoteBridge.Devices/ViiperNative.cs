using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

internal static class ViiperNative
{
    internal const string Revision = "a77bbbb78a6072188fe40526acbcba5d9d70d197";
    private const string Library = "libVIIPER";
    private static nint loaded;
    internal static void LoadVerified()
    {
        if (loaded != 0) return;
        string path = Path.Combine(AppContext.BaseDirectory, "native", "libVIIPER.dll");
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "native", "manifest.json");
        if (!File.Exists(path) || !File.Exists(manifestPath))
            throw new FileNotFoundException("Verified libVIIPER package is missing. Install a complete reviewed release.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        if (root.GetProperty("sourceRevision").GetString() != Revision) throw new InvalidDataException("Unsupported libVIIPER source revision.");
        string expected = root.GetProperty("sha256").GetString() ?? "";
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("libVIIPER integrity check failed.");
        // Absolute path plus secure Windows dependency search: never search the working directory.
        loaded = LoadLibraryEx(path, 0, 0x100 | 0x800);
        if (loaded == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot load x64 libVIIPER.");
        NativeLibrary.SetDllImportResolver(typeof(ViiperNative).Assembly, (name, _, _) => name == Library ? loaded : 0);
    }
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct ServerConfig
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Address;
        public ulong ConnectionTimeout, DeviceTimeout;
        public uint BatchInterval;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Metadata
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Serial;
        public nint Board;
        public byte Battery;
        public double Temperature, Voltage;
    }
    // Native C alignment, not the packed 31-byte TCP wire layout. Size = 34.
    [StructLayout(LayoutKind.Sequential)] internal struct State
    {
        public sbyte LX, LY, RX, RY;
        public ushort Buttons;
        public byte DPad, L2, R2;
        public ushort Touch1X, Touch1Y;
        public byte Touch1Active;
        public ushort Touch2X, Touch2Y;
        public byte Touch2Active;
        public short GX, GY, GZ, AX, AY, AZ;
        public static State From(DS4State s) => new()
        {
            LX = s.LX, LY = s.LY, RX = s.RX, RY = s.RY, Buttons = (ushort)s.Buttons, DPad = s.DPad, L2 = s.L2, R2 = s.R2,
            Touch1X = s.Touch.First.X, Touch1Y = s.Touch.First.Y, Touch1Active = (byte)(s.Touch.First.Active ? 1 : 0),
            Touch2X = s.Touch.Second.X, Touch2Y = s.Touch.Second.Y, Touch2Active = (byte)(s.Touch.Second.Active ? 1 : 0),
            GX = s.Motion.GX, GY = s.Motion.GY, GZ = s.Motion.GZ, AX = s.Motion.AX, AY = s.Motion.AY, AZ = s.Motion.AZ
        };
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Output(nuint device, byte flags, byte small, byte large, byte red, byte green, byte blue, byte on, byte off);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool NewUSBServer(ref ServerConfig config, out nuint handle, nint logger);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CloseUSBServer(nuint handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateUSBBus(nuint handle, ref uint bus);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CreateDS4(nuint server, out nuint device, uint bus, [MarshalAs(UnmanagedType.I1)] bool attach, ushort vid, ushort pid, ref Metadata metadata);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetDS4DeviceState(nuint device, State state);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SetDS4OutputCallback(nuint device, Output? callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RemoveDS4(nuint device);
}
