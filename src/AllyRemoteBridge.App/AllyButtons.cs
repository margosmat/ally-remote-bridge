using System.Runtime.InteropServices;
using System.Text;

namespace AllyRemoteBridge.App;

internal sealed class AllyButtons : NativeWindow, IDisposable
{
    private readonly Dictionary<nint, bool> knownDevices = new();
    private long pressedUntil;
    private volatile bool paddleHeld;
    private readonly Action<string> log;

    internal AllyButtons(Action<string> log)
    {
        this.log = log;
        CreateHandle(new CreateParams { Caption = "Ally Remote Bridge Raw Input" });
        var registration = new Registration { Page = 0xFF31, Usage = 0x76, Flags = 0x100, Target = Handle };
        if (!RegisterRawInputDevices([registration], 1, (uint)Marshal.SizeOf<Registration>()))
            log($"ASUS button registration failed (Win32 {Marshal.GetLastWin32Error()}).");
        else
            log("ASUS button listener registered.");
    }

    internal bool IsPressed() => paddleHeld || Environment.TickCount64 < Interlocked.Read(ref pressedUntil);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x00FF) ReadAllyButtons(message.LParam);
        base.WndProc(ref message);
    }

    private void ReadAllyButtons(nint raw)
    {
        uint headerSize = (uint)Marshal.SizeOf<Header>(), size = 0;
        if (GetRawInputData(raw, 0x10000003, null, ref size, headerSize) != 0 || size < headerSize + 14 || size > 4096)
            return;
        byte[] data = new byte[size];
        if (GetRawInputData(raw, 0x10000003, data, ref size, headerSize) != size) return;
        var header = MemoryMarshal.Read<Header>(data);
        if (header.Type != 2) return;
        var payload = data.AsSpan((int)headerSize);
        int reportSize = MemoryMarshal.Read<int>(payload);
        int count = MemoryMarshal.Read<int>(payload[4..]);
        if (reportSize != 6 || count <= 0 || count > (payload.Length - 8) / reportSize) return;
        if (!knownDevices.TryGetValue(header.Device, out bool ally))
        {
            uint length = 0;
            GetRawInputDeviceInfo(header.Device, 0x20000007, null, ref length);
            if (length is 0 or > 4096) return;
            var name = new StringBuilder((int)length);
            ally = GetRawInputDeviceInfo(header.Device, 0x20000007, name, ref length) != uint.MaxValue &&
                name.ToString().Contains("VID_0B05&PID_1B4C&MI_02&Col01", StringComparison.OrdinalIgnoreCase);
            knownDevices[header.Device] = ally;
        }
        if (!ally) return;
        for (int i = 0; i < count; i++)
        {
            var report = payload.Slice(8 + i * reportSize, reportSize);
            if (report[0] != 0x5A) continue;
            switch (report[1])
            {
                case 0x93:
                    Interlocked.Exchange(ref pressedUntil, Environment.TickCount64 + 100);
                    log("ASUS Library button received.");
                    break;
                case 0xA5 when !paddleHeld:
                    // Keep quick taps visible to the polling loop as well.
                    Interlocked.Exchange(ref pressedUntil, Environment.TickCount64 + 100);
                    paddleHeld = true;
                    log("ASUS paddle pressed.");
                    break;
                case 0x00 when paddleHeld:
                    paddleHeld = false;
                    log("ASUS paddle released.");
                    break;
            }
        }
    }

    public void Dispose()
    {
        var registration = new Registration { Page = 0xFF31, Usage = 0x76, Flags = 1 };
        RegisterRawInputDevices([registration], 1, (uint)Marshal.SizeOf<Registration>());
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public uint Type, Size; public nint Device, Parameter; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Registration { public ushort Page, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(Registration[] registrations, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint raw, uint command, [Out] byte[]? buffer, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? name, ref uint size);
}
