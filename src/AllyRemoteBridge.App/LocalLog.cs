using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.App;

internal sealed class LocalLog
{
    private readonly object gate = new();
    public string PathName { get; } = Path.Combine(SettingsStore.DirectoryPath, "bridge.log");
    public void Write(string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 1024 * 1024) File.Move(PathName, PathName + ".old", true);
                File.AppendAllText(PathName, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
