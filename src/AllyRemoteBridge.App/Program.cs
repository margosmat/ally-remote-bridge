using System.Runtime.InteropServices;
using AllyRemoteBridge.Core;
using AllyRemoteBridge.Devices;

namespace AllyRemoteBridge.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        { MessageBox.Show("Ally Remote Bridge requires Windows 11 x64.", "Unsupported platform"); return 1; }
        using var mutex = new Mutex(true, @"Local\AllyRemoteBridge", out bool first);
        if (!first) { MessageBox.Show("Ally Remote Bridge is already running. Use its tray icon to open settings or stop emulation."); return 1; }
        var log = new LocalLog();
        try
        {
            if (args.Length > 1 || args.Any(a => a is not ("--settings" or "--cleanup" or "--diagnostics" or "--install-driver" or "--installer-cleanup" or "--verify-device")))
                throw new ArgumentException("Supported options: --settings, --cleanup, --diagnostics.");
            if (args.Contains("--install-driver")) { DriverInstaller.InstallReviewedPackage(); return 0; }
            if (args.Contains("--cleanup") || args.Contains("--installer-cleanup"))
            {
                ViiperDS4.Cleanup();
                if (!args.Contains("--installer-cleanup")) MessageBox.Show("Bridge-owned device cleanup completed.", "Ally Remote Bridge"); return 0;
            }
            if (args.Contains("--verify-device"))
            {
                using var device = new ViiperDS4(log.Write);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                device.AttachAsync(timeout.Token).GetAwaiter().GetResult();
                device.Send(DS4State.Neutral);
                device.Detach();
                MessageBox.Show("Virtual DS4 attachment, neutral report, and removal verified. Sony compatibility and physical controls still require the acceptance tests.", "Hardware integration check");
                return 0;
            }
            var settings = SettingsStore.Load();
            if (args.Contains("--settings")) { Application.Run(new SettingsWindow(settings, false)); return 0; }
            if (args.Contains("--diagnostics"))
            {
                using var sensors = new SensorReader();
                sensors.Start();
                MessageBox.Show($"XInput slots: {string.Join(", ", XInputController.ConnectedSlots())}\n{sensors.Status}\n\nLocal log: {log.PathName}", "Ally Remote Bridge diagnostics");
                return 0;
            }
            using var context = new BridgeContext(settings, log);
            Application.Run(context);
            return context.ExitCode;
        }
        catch (Exception e)
        {
            log.Write($"Startup failed: {e.GetType().Name}: {e.Message}");
            if (args.Contains("--installer-cleanup")) return 1;
            MessageBox.Show(e.Message + "\n\nOpen Ally Remote Bridge Settings to review configuration.", "Ally Remote Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
