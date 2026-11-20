using AllyRemoteBridge.Core;
using AllyRemoteBridge.Devices;
using AllyRemoteBridge.Overlay;
using Microsoft.Win32;

namespace AllyRemoteBridge.App;

internal sealed class BridgeContext : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly TouchpadWindow overlay;
    private readonly SensorReader sensors = new();
    private readonly ViiperDS4 device;
    private readonly RemotePlayProcess remote;
    private readonly SessionManager session;
    private readonly CancellationTokenSource cancellation = new();
    private readonly LocalLog log;
    private readonly BridgeSettings settings;
    private readonly System.Windows.Forms.Timer guard = new() { Interval = 100 };
    private Task? work;
    private bool stopping;
    public int ExitCode { get; private set; }
    public BridgeContext(BridgeSettings settings, LocalLog log)
    {
        this.settings = settings; this.log = log;
        overlay = new(settings); device = new(log.Write); remote = new(settings);
        session = new(settings, new XInputController(settings.ControllerIndex), sensors, device, remote, overlay, log.Write);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle touchpad", null, (_, _) => { if (settings.EnableTouchpad && session.IsForwarding) overlay.Toggle(); });
        menu.Items.Add("Settings…", null, (_, _) => { using var window = new SettingsWindow(settings, true); window.ShowDialog(); });
        menu.Items.Add("Diagnostics…", null, (_, _) => MessageBox.Show($"Session: {session.State}\n{sensors.Status}\nLocal log: {log.PathName}", "Ally Remote Bridge"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Stop emulation and exit", null, (_, _) => Stop());
        tray = new() { Icon = SystemIcons.Application, Text = "Ally Remote Bridge", ContextMenuStrip = menu, Visible = true };
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionEnding += OnSessionEnding;
        guard.Tick += (_, _) => { if (!session.IsForwarding) overlay.Deactivate(); };
        guard.Start();
        Application.Idle += StartOnce;
    }
    private async void StartOnce(object? sender, EventArgs e)
    {
        Application.Idle -= StartOnce;
        try
        {
            work = Task.Run(() => session.RunAsync(cancellation.Token));
            await work;
        }
        catch (Exception error)
        {
            ExitCode = 1;
            log.Write($"Session failed: {error.GetType().Name}: {error.Message}");
            MessageBox.Show(error.Message, "Ally Remote Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { ExitThread(); }
    }
    private void Stop() { if (stopping) return; stopping = true; cancellation.Cancel(); overlay.Deactivate(); }
    private void OnPower(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) session.Suspend(true);
        if (args.Mode == PowerModes.Resume) session.Suspend(false);
    }
    private void OnSessionEnding(object sender, SessionEndingEventArgs args) => Stop();
    protected override void ExitThreadCore()
    {
        guard.Stop(); guard.Dispose();
        SystemEvents.PowerModeChanged -= OnPower;
        SystemEvents.SessionEnding -= OnSessionEnding;
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
        overlay.Dispose(); sensors.Dispose(); remote.Dispose();
        try { device.Dispose(); } catch (Exception e) { log.Write($"Final cleanup failed: {e.Message}"); }
        cancellation.Dispose();
        base.ExitThreadCore();
    }
}
