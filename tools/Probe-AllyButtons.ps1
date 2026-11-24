$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public sealed class AllyButtonProbe : Form
{
    [StructLayout(LayoutKind.Sequential)]
    struct Device { public IntPtr Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)]
    struct Registration { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
    [StructLayout(LayoutKind.Sequential)]
    struct Header { public uint Type, Size; public IntPtr Device, Param; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputDeviceList([Out] Device[] devices, ref uint count, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern uint GetRawInputDeviceInfoW(IntPtr device, uint command, StringBuilder data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);

    readonly HashSet<IntPtr> sources = new HashSet<IntPtr>();
    readonly List<string> lines = new List<string>();
    readonly string log;
    readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    public AllyButtonProbe(string logPath)
    {
        log = logPath;
        Text = "Ally button probe";
        Width = 750;
        Height = 380;
        Controls.Add(output);
        Shown += delegate
        {
            try { Start(); }
            catch (Exception error) { Record("Startup failed: " + error.Message); }
        };
    }

    void Record(string line)
    {
        lines.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line);
        System.IO.File.AppendAllText(log, lines[lines.Count - 1] + Environment.NewLine);
        output.AppendText(lines[lines.Count - 1] + Environment.NewLine);
    }

    void Start()
    {
        uint count = 0, size = (uint)Marshal.SizeOf(typeof(Device));
        if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new System.ComponentModel.Win32Exception();
        var devices = new Device[count];
        if (GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue) throw new System.ComponentModel.Win32Exception();
        var registrations = new Dictionary<int, Registration>();
        foreach (var device in devices)
        {
            uint length = 0;
            GetRawInputDeviceInfo(device.Handle, 0x20000007, IntPtr.Zero, ref length);
            if (length == 0) continue;
            var name = new StringBuilder((int)length);
            if (GetRawInputDeviceInfoW(device.Handle, 0x20000007, name, ref length) == uint.MaxValue) continue;
            if (name.ToString().IndexOf("VID_0B05&PID_1B4C", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (device.Type != 1 && device.Type != 2) continue;
            ushort page = 1, usage = 6;
            if (device.Type == 2)
            {
                uint infoSize = 32;
                IntPtr info = Marshal.AllocHGlobal((int)infoSize);
                try
                {
                    Marshal.WriteInt32(info, (int)infoSize);
                    if (GetRawInputDeviceInfo(device.Handle, 0x2000000B, info, ref infoSize) == uint.MaxValue) continue;
                    page = (ushort)Marshal.ReadInt16(info, 20);
                    usage = (ushort)Marshal.ReadInt16(info, 22);
                }
                finally { Marshal.FreeHGlobal(info); }
            }
            sources.Add(device.Handle);
            registrations[(page << 16) | usage] = new Registration { Page = page, Usage = usage, Flags = 0x100, Window = Handle };
            Record("Device " + device.Handle + " " + name + " usage " + page.ToString("X4") + ":" + usage.ToString("X4"));
        }
        foreach (var registration in registrations.Values)
            if (!RegisterRawInputDevices(new[] { registration }, 1, (uint)Marshal.SizeOf(typeof(Registration))))
                Record("Registration failed for usage " + registration.Page.ToString("X4") + ":" + registration.Usage.ToString("X4") + " error " + Marshal.GetLastWin32Error());
        Record("Ready. Press Library, then M1, then M2. Pause between presses.");
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x00FF) ReadInput(message.LParam);
        base.WndProc(ref message);
    }

    void ReadInput(IntPtr raw)
    {
        uint size = 0, headerSize = (uint)Marshal.SizeOf(typeof(Header));
        GetRawInputData(raw, 0x10000003, IntPtr.Zero, ref size, headerSize);
        if (size < headerSize) return;
        IntPtr data = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(raw, 0x10000003, data, ref size, headerSize) != size) return;
            var header = (Header)Marshal.PtrToStructure(data, typeof(Header));
            if (!sources.Contains(header.Device)) return;
            IntPtr payload = IntPtr.Add(data, (int)headerSize);
            if (header.Type == 1 && size - headerSize >= 16)
            {
                int key = (ushort)Marshal.ReadInt16(payload, 6);
                int flags = (ushort)Marshal.ReadInt16(payload, 2);
                Record("Key " + header.Device + " VK=" + key.ToString("X2") + " flags=" + flags.ToString("X"));
            }
            else if (header.Type == 2 && size - headerSize >= 8)
            {
                byte[] bytes = new byte[size - headerSize - 8];
                Marshal.Copy(IntPtr.Add(payload, 8), bytes, 0, bytes.Length);
                Record("HID " + header.Device + " " + BitConverter.ToString(bytes));
            }
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
'@

$log = Join-Path $PSScriptRoot '..\artifacts\ally-buttons.log'
New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null
Set-Content -LiteralPath $log -Value 'Ally Raw Input probe'
[System.Windows.Forms.Application]::Run([AllyButtonProbe]::new($log))
