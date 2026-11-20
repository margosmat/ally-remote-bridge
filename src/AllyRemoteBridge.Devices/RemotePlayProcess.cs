using System.Diagnostics;
using System.Runtime.InteropServices;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

public sealed class RemotePlayProcess(BridgeSettings settings) : IRemotePlay
{
    private string? path;
    private Process? process;
    private DateTime nextDiscovery;
    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = settings.RemotePlayPath ?? new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Sony", "PS Remote Play", "RemotePlay.exe"))
            .FirstOrDefault(File.Exists);
        if (path is null || !File.Exists(path)) throw new FileNotFoundException("Official PS Remote Play was not found. Select RemotePlay.exe in settings.");
        if (!Path.GetFileName(path).Equals("RemotePlay.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select Sony's RemotePlay.exe.");
        path = Path.GetFullPath(path);
        Authenticode.VerifySony(path);
        Discover();
        return Task.CompletedTask;
    }
    public async Task LaunchAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            if (!settings.AutoLaunch) throw new InvalidOperationException("Start PS Remote Play first or enable automatic launch.");
            process?.Dispose();
            process = Process.Start(new ProcessStartInfo(path!) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! });
        }
        for (int i = 0; i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRunning && process!.MainWindowHandle != 0) return;
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("Remote Play did not open its window within 10 seconds.");
    }
    private bool Matches(Process p)
    {
        try { return !p.HasExited && string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
    private void Discover()
    {
        foreach (var candidate in Process.GetProcessesByName("RemotePlay"))
        {
            if (Matches(candidate))
            {
                Authenticode.VerifySony(path!); // Recheck replacement executable after client updates.
                process?.Dispose(); process = candidate; return;
            }
            candidate.Dispose();
        }
    }
    public bool IsRunning
    {
        get
        {
            if (process is not null && !process.HasExited) return true;
            if (DateTime.UtcNow >= nextDiscovery)
            {
                nextDiscovery = DateTime.UtcNow.AddMilliseconds(500);
                Discover();
            }
            return process is not null && !process.HasExited;
        }
    }
    public bool IsForeground
    {
        get
        {
            if (process is null || process.HasExited) return false;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint id);
            return id == process!.Id;
        }
    }
    public void Dispose() { process?.Dispose(); process = null; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
