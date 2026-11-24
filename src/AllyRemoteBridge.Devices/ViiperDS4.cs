using System.Security.Cryptography;
using System.Text.Json;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

public sealed class ViiperDS4(Action<string> log) : IVirtualController
{
    private nuint server, device;
    private UsbIpClient? client;
    private Ownership? ownership;
    private ViiperNative.Output? callback;
    public event Action<byte, byte>? RumbleReceived;
    private static string Journal => Path.Combine(SettingsStore.DirectoryPath, "device-ownership.json");
    private sealed record Ownership(uint Bus, string Serial, int? Port);

    public async Task AttachAsync(CancellationToken cancellationToken)
    {
        if (server != 0) throw new InvalidOperationException("Virtual controller already exists.");
        ViiperNative.LoadVerified();
        client = new UsbIpClient();
        try
        {
            Cleanup();
            var config = new ViiperNative.ServerConfig { Address = "127.0.0.1:32451", ConnectionTimeout = 5000, DeviceTimeout = 5000, BatchInterval = 1 };
            Check(ViiperNative.NewUSBServer(ref config, out server, 0), "start loopback USB/IP server");
            uint bus = (uint)RandomNumberGenerator.GetInt32(1000, 60000);
            Check(ViiperNative.CreateUSBBus(server, ref bus), "create USB bus");
            string serial = "A11" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)); // 15 ASCII characters
            ownership = new(bus, serial, null);
            SaveOwnership(ownership); // Persist intent BEFORE attachment, including crash during IOCTL.
            var meta = new ViiperNative.Metadata { Serial = serial.PadLeft(16, '0') };
            Check(ViiperNative.CreateDS4(server, out device, bus, false, 0, 0, ref meta), "create DS4");
            callback = (_, flags, small, large, _, _, _, _, _) =>
            {
                try { if ((flags & 1) != 0) RumbleReceived?.Invoke(small, large); }
                catch { /* Never propagate a managed exception through a native callback. */ }
            };
            Check(ViiperNative.SetDS4OutputCallback(device, callback), "register DS4 output callback");
            cancellationToken.ThrowIfCancellationRequested();
            int port = await Task.Run(() => client.Attach($"{bus}-1", serial), CancellationToken.None);
            ownership = ownership with { Port = port };
            SaveOwnership(ownership);
            // Confirm the uniquely identified HID is present before starting Sony.
            for (int i = 0; i < 100; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HidDiscovery.HasDS4(serial, client.ControllerNode, port)) { log("Virtual DS4 detected by Windows"); return; }
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException("The bridge could not identify the virtual DS4 on its USB/IP port within 10 seconds.");
        }
        catch { Detach(); throw; }
    }
    private static void SaveOwnership(Ownership value)
    {
        Directory.CreateDirectory(SettingsStore.DirectoryPath);
        File.WriteAllText(Journal + ".tmp", JsonSerializer.Serialize(value));
        File.Move(Journal + ".tmp", Journal, true);
    }
    public static void Cleanup()
    {
        if (!File.Exists(Journal)) return;
        var owned = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(Journal)) ?? throw new InvalidDataException("Invalid device ownership journal.");
        if (owned.Bus is < 1000 or >= 60000 || owned.Serial.Length != 15 || !owned.Serial.StartsWith("A11", StringComparison.Ordinal) || !owned.Serial.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid ownership record; no device removed.");
        using var usbip = new UsbIpClient();
        usbip.DetachOwned($"{owned.Bus}-1", owned.Serial, owned.Port);
        for (int i = 0; i < 40 && HidDiscovery.HasDS4(owned.Serial, usbip.ControllerNode, owned.Port); i++) Thread.Sleep(50);
        if (HidDiscovery.HasDS4(owned.Serial, usbip.ControllerNode, owned.Port)) throw new IOException("The bridge-owned DS4 is still present. Ownership journal retained.");
        File.Delete(Journal);
    }
    public void Send(DS4State state)
    {
        if (device == 0) return;
        Check(ViiperNative.SetDS4DeviceState(device, ViiperNative.State.From(state)), "send DS4 input");
    }
    public void Detach()
    {
        List<Exception> errors = [];
        void Attempt(Action action) { try { action(); } catch (Exception e) { errors.Add(e); } }
        if (device != 0)
        {
            Attempt(() => Check(ViiperNative.SetDS4OutputCallback(device, null), "clear output callback"));
            Attempt(() => Send(DS4State.Neutral));
        }
        if (ownership is { } owned && client is not null)
            Attempt(() => { client.DetachOwned($"{owned.Bus}-1", owned.Serial, owned.Port); });
        if (device != 0) Attempt(() => Check(ViiperNative.RemoveDS4(device), "remove DS4"));
        device = 0;
        if (server != 0) Attempt(() => Check(ViiperNative.CloseUSBServer(server), "close USB/IP server"));
        server = 0;
        if (ownership is { } removed)
            Attempt(() =>
            {
                for (int i = 0; i < 40 && HidDiscovery.HasDS4(removed.Serial, client!.ControllerNode, removed.Port); i++) Thread.Sleep(50);
                if (HidDiscovery.HasDS4(removed.Serial, client!.ControllerNode, removed.Port)) throw new IOException("Windows still reports the bridge-owned DS4; ownership journal retained.");
                if (errors.Count == 0) File.Delete(Journal);
            });
        client?.Dispose(); client = null; ownership = null;
        GC.KeepAlive(callback); callback = null;
        if (errors.Count != 0) throw new AggregateException("Device cleanup failed. Run --cleanup before another session.", errors);
    }
    private static void Check(bool success, string operation) { if (!success) throw new InvalidOperationException($"libVIIPER failed to {operation}."); }
    public void Dispose() => Detach();
}
