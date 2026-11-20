using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using AllyRemoteBridge.Core;
using AllyRemoteBridge.Devices;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action body) => tests.Add((name, () => { body(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> body) => tests.Add((name, body));
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
DS4State Map(DS4Mapper mapper, XboxButtons buttons, int ms = 0) => mapper.Map(new(buttons, 0, 0, 0, 0, 0, 0), TimeSpan.FromMilliseconds(ms), default, MotionState.Neutral);

Test("All basic buttons map simultaneously", () =>
{
    var mapper = new DS4Mapper(new());
    var result = Map(mapper, XboxButtons.A | XboxButtons.B | XboxButtons.X | XboxButtons.Y | XboxButtons.LB | XboxButtons.RB | XboxButtons.LS | XboxButtons.RS | XboxButtons.Menu);
    Equal(DS4Buttons.Cross | DS4Buttons.Circle | DS4Buttons.Square | DS4Buttons.Triangle | DS4Buttons.L1 | DS4Buttons.R1 | DS4Buttons.L3 | DS4Buttons.R3 | DS4Buttons.Options, result.Buttons);
});
Test("Analog range and Y inversion", () =>
{
    var result = new DS4Mapper(new()).Map(new(0, 255, 128, short.MinValue, short.MaxValue, short.MaxValue, 0), default, default, MotionState.Neutral);
    Equal((sbyte)127, result.RX); Equal((sbyte)0, result.RY); Equal((byte)255, result.L2); Equal((byte)128, result.R2);
    True(result.Buttons.HasFlag(DS4Buttons.L2 | DS4Buttons.R2)); True(result.LX < 0 && result.LY < 0);
});
Test("Dead zone neutralizes stick drift", () =>
{
    var result = new DS4Mapper(new()).Map(new(0, 0, 0, 200, -100, 0, 0), default, default, MotionState.Neutral);
    Equal((sbyte)0, result.LX); Equal((sbyte)0, result.LY);
});
Test("D-pad uses VIIPER direction bits and cancels opposites", () =>
{
    var mapper = new DS4Mapper(new());
    Equal((byte)9, Map(mapper, XboxButtons.Up | XboxButtons.Right).DPad);
    Equal((byte)0, Map(mapper, XboxButtons.Up | XboxButtons.Down | XboxButtons.Left | XboxButtons.Right).DPad);
});
Test("View tap emits one Share pulse on release", () =>
{
    var mapper = new DS4Mapper(new());
    Equal(DS4Buttons.None, Map(mapper, XboxButtons.View).Buttons);
    Equal(DS4Buttons.Share, Map(mapper, 0, 10).Buttons);
    Equal(DS4Buttons.None, Map(mapper, 0, 14).Buttons);
});
Test("Touch click chord never leaks Share or Square", () =>
{
    var mapper = new DS4Mapper(new());
    Map(mapper, XboxButtons.View);
    Equal(DS4Buttons.Touchpad, Map(mapper, XboxButtons.View | XboxButtons.X, 10).Buttons);
    Equal(DS4Buttons.None, Map(mapper, XboxButtons.X, 20).Buttons);
    Equal(DS4Buttons.None, Map(mapper, 0, 30).Buttons);
    Equal(DS4Buttons.Square, Map(mapper, XboxButtons.X, 40).Buttons);
});
Test("PS requires holding the complete chord", () =>
{
    var mapper = new DS4Mapper(new());
    Map(mapper, XboxButtons.View);
    Equal(DS4Buttons.None, Map(mapper, XboxButtons.View | XboxButtons.Menu, 2000).Buttons);
    Equal(DS4Buttons.PS, Map(mapper, XboxButtons.View | XboxButtons.Menu, 2600).Buttons);
    Equal(DS4Buttons.None, Map(mapper, XboxButtons.Menu, 2700).Buttons);
});
Test("Overlay chord fires once until prefix release", () =>
{
    var mapper = new DS4Mapper(new());
    Map(mapper, XboxButtons.View | XboxButtons.Y); True(mapper.OverlayRequested);
    Map(mapper, XboxButtons.View | XboxButtons.Y, 10); True(!mapper.OverlayRequested);
    Map(mapper, 0, 20); Map(mapper, XboxButtons.View | XboxButtons.Y, 30); True(mapper.OverlayRequested);
});
Test("Reset discards pending shortcuts", () =>
{
    var mapper = new DS4Mapper(new()); Map(mapper, XboxButtons.View); mapper.Reset(); Equal(DS4Buttons.None, Map(mapper, 0).Buttons);
});
Test("Touch bounds clamp to DS4 surface", () =>
{
    var touch = new TouchManager(); touch.Update(99, 600, -20, 480, 236);
    Equal(new TouchContact(1920, 0, true), touch.Read().First);
});
Test("Two contacts preserve slots when first finger lifts", () =>
{
    var touch = new TouchManager(); touch.Update(10, 10, 10, 100, 100); touch.Update(20, 20, 20, 100, 100);
    var before = touch.Read(); touch.Release(10); var after = touch.Read();
    Equal(default(TouchContact), after.First); Equal(before.Second, after.Second);
    touch.Update(30, 30, 30, 100, 100); touch.Update(40, 40, 40, 100, 100);
    Equal(before.Second, touch.Read().Second); touch.Reset(); Equal(default(TouchState), touch.Read());
});
Test("Motion retains gravity and scales physical units", () =>
{
    var now = DateTimeOffset.UtcNow;
    var result = new MotionManager(new()).Convert(new(new(1, 2, 3), new(0, 0, -9.81f), now), now);
    Equal((short)16, result.GX); Equal((short)32, result.GY); Equal((short)-8192, result.AZ);
});
Test("Motion axis permutation and bias", () =>
{
    var now = DateTimeOffset.UtcNow;
    var result = new MotionManager(new() { MotionAxes = [-2, 3, 1], GyroBias = [1, 0, 0] }).Convert(new(new(2, 3, 4), Vector3.Zero, now), now);
    Equal((short)-48, result.GX); Equal((short)64, result.GY); Equal((short)16, result.GZ);
});
Test("Invalid or stale motion becomes neutral", () =>
{
    var now = DateTimeOffset.UtcNow; var mapper = new MotionManager(new());
    Equal(MotionState.Neutral, mapper.Convert(new(new(float.NaN, 0, 0), Vector3.Zero, now), now));
    Equal(MotionState.Neutral, mapper.Convert(new(Vector3.One, Vector3.One, now.AddSeconds(-1)), now));
});
Test("Motion saturates safely", () =>
{
    var now = DateTimeOffset.UtcNow;
    var result = new MotionManager(new()).Convert(new(new(10000, -10000, 0), new(100, -100, 0), now), now);
    Equal(short.MaxValue, result.GX); Equal(short.MinValue, result.GY); Equal(short.MaxValue, result.AX);
});
Test("Settings reject invalid configuration", () =>
{
    Throws<InvalidDataException>(() => new BridgeSettings { DeadZone = double.NaN }.Validate());
    Throws<InvalidDataException>(() => new BridgeSettings { MotionAxes = [1, 1, 2] }.Validate());
    Throws<InvalidDataException>(() => new BridgeSettings { PSButton = XboxButtons.X }.Validate());
    Throws<InvalidDataException>(() => new BridgeSettings { SchemaVersion = 2 }.Validate());
});
Test("Settings persist and validate round trip", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "ally-tests-" + Guid.NewGuid());
    var path = Path.Combine(directory, "settings.json");
    try { SettingsStore.Save(new() { DeadZone = .12, ControllerIndex = 2 }, path); Equal(.12, SettingsStore.Load(path).DeadZone); Equal((uint?)2, SettingsStore.Load(path).ControllerIndex); }
    finally { Directory.Delete(directory, true); }
});
Test("Native DS4 ABI uses C padding", () =>
{
    Equal(34, Marshal.SizeOf<ViiperNative.State>());
    Equal(new nint(10), Marshal.OffsetOf<ViiperNative.State>("Touch1X"));
    Equal(new nint(16), Marshal.OffsetOf<ViiperNative.State>("Touch2X"));
    Equal(new nint(22), Marshal.OffsetOf<ViiperNative.State>("GX"));
    Equal(32, Marshal.SizeOf<ViiperNative.ServerConfig>());
});
Test("USB/IP attach request has expected ABI fields", () =>
{
    byte[] request = UsbIpProtocol.Attach("1234-1", "ARB123456789012");
    Equal(1120, request.Length); Equal(1120, BitConverter.ToInt32(request));
    Equal("127.0.0.1", System.Text.Encoding.ASCII.GetString(request, 76, 9));
    Throws<ArgumentException>(() => UsbIpProtocol.Attach("1234-1", new string('A', 16)));
});
Test("USB/IP recovery matches all ownership fields", () =>
{
    byte[] list = new byte[4 + 1132 * 2];
    void Entry(int offset, string serial, int port)
    {
        BitConverter.GetBytes(port).CopyTo(list, offset);
        System.Text.Encoding.ASCII.GetBytes("1234-1").CopyTo(list, offset + 8);
        System.Text.Encoding.ASCII.GetBytes("32451").CopyTo(list, offset + 40);
        System.Text.Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(list, offset + 72);
        System.Text.Encoding.ASCII.GetBytes(serial).CopyTo(list, offset + 1097);
    }
    Entry(4, "ARB123456789012", 7); Entry(4 + 1132, "OTHERDEVICE", 8);
    Equal(7, UsbIpProtocol.OwnedPorts(list, (uint)list.Length, "1234-1", "ARB123456789012", 7).Single());
    Equal(0, UsbIpProtocol.OwnedPorts(list, (uint)list.Length, "1234-1", "ARB123456789012", 8).Count);
    Equal(0, UsbIpProtocol.OwnedPorts(list, (uint)list.Length, "9999-1", "ARB123456789012", null).Count);
    Throws<InvalidDataException>(() => UsbIpProtocol.OwnedPorts(list, (uint)list.Length - 1, "1234-1", "ARB123456789012", null));
    Throws<ArgumentOutOfRangeException>(() => UsbIpProtocol.Detach(0));
    Throws<ArgumentOutOfRangeException>(() => UsbIpProtocol.Detach(-1));
});
Test("Disabled rumble never touches physical motors", () =>
{
    var controller = new FakeController(); var rumble = new RumbleManager(false, controller, _ => { });
    rumble.SetActive(true); rumble.Forward(20, 30); rumble.SetActive(false);
    Equal(0, controller.RumbleCalls);
});
Test("Active rumble stops exactly once on focus loss", () =>
{
    var controller = new FakeController(); var rumble = new RumbleManager(true, controller, _ => { });
    rumble.Forward(20, 30); Equal(0, controller.RumbleCalls);
    rumble.SetActive(true); rumble.Forward(20, 30); Equal(1, controller.RumbleCalls);
    rumble.SetActive(false); Equal(1, controller.RumbleStops);
    rumble.SetActive(false); Equal(2, controller.RumbleCalls);
});
AsyncTest("Session orders attach before Sony launch and cleans on cancel", async () =>
{
    using var fixture = new Fixture();
    var task = fixture.Session.RunAsync(fixture.Cancel.Token);
    await fixture.WaitForInput();
    True(fixture.Events.IndexOf("attach") < fixture.Events.IndexOf("launch"));
    fixture.Cancel.Cancel(); await task;
    Equal(SessionState.Idle, fixture.Session.State); Equal(DS4State.Neutral, fixture.Device.States.Last()); True(fixture.Device.Detached); True(fixture.Motion.Stopped);
});
AsyncTest("Focus loss clears held buttons and rumble", async () =>
{
    using var fixture = new Fixture(); var task = fixture.Session.RunAsync(fixture.Cancel.Token);
    await fixture.WaitForInput(); fixture.Device.EmitRumble(20, 30); fixture.Remote.Foreground = false;
    await Until(() => fixture.Device.States.LastOrDefault() == DS4State.Neutral);
    True(!fixture.Session.IsForwarding); True(fixture.Controller.RumbleStops > 0);
    fixture.Cancel.Cancel(); await task;
});
AsyncTest("Controller disconnect clears input without switching devices", async () =>
{
    using var fixture = new Fixture(); var task = fixture.Session.RunAsync(fixture.Cancel.Token);
    await fixture.WaitForInput(); fixture.Controller.Connected = false;
    await Until(() => fixture.Device.States.LastOrDefault() == DS4State.Neutral);
    fixture.Cancel.Cancel(); await task;
});
AsyncTest("Sony exit ends session and removes controller", async () =>
{
    using var fixture = new Fixture(); var task = fixture.Session.RunAsync(fixture.Cancel.Token);
    await fixture.WaitForInput(); fixture.Remote.Running = false; await task.WaitAsync(TimeSpan.FromSeconds(2));
    True(fixture.Device.Detached); Equal(SessionState.Idle, fixture.Session.State);
});
AsyncTest("Failed attachment still triggers cleanup", async () =>
{
    using var fixture = new Fixture(); fixture.Device.FailAttach = true;
    try { await fixture.Session.RunAsync(fixture.Cancel.Token); throw new Exception("Expected attach failure"); } catch (IOException) { }
    True(fixture.Device.Detached); True(!fixture.Events.Contains("launch"));
});
AsyncTest("Report failure still detaches even when neutral report fails", async () =>
{
    using var fixture = new Fixture(); fixture.Device.FailSend = true;
    try { await fixture.Session.RunAsync(fixture.Cancel.Token); throw new Exception("Expected report failure"); } catch (AggregateException) { }
    True(fixture.Device.Detached); Equal(SessionState.Idle, fixture.Session.State);
});
AsyncTest("Suspend pauses sensors and neutralizes input; resume recovers", async () =>
{
    using var fixture = new Fixture(); var task = fixture.Session.RunAsync(fixture.Cancel.Token);
    await fixture.WaitForInput(); fixture.Session.Suspend(true);
    await Until(() => fixture.Motion.Stopped && fixture.Device.States.LastOrDefault() == DS4State.Neutral);
    fixture.Session.Suspend(false); await Until(() => fixture.Motion.Starts == 2 && fixture.Session.IsForwarding);
    Equal(2, fixture.Events.Count(e => e == "attach"));
    fixture.Cancel.Cancel(); await task;
});
AsyncTest("Duplicate session cannot create another controller", async () =>
{
    using var fixture = new Fixture(); var task = fixture.Session.RunAsync(fixture.Cancel.Token); await fixture.WaitForInput();
    try { await fixture.Session.RunAsync(fixture.Cancel.Token); throw new Exception("Expected single-instance rejection"); } catch (InvalidOperationException) { }
    Equal(1, fixture.Events.Count(e => e == "attach")); fixture.Cancel.Cancel(); await task;
});

int failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed");
return failures == 0 ? 0 : 1;

static async Task Until(Func<bool> predicate)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Test condition timed out"); await Task.Delay(5); }
}
sealed class Fixture : IDisposable
{
    public readonly List<string> Events = [];
    public readonly FakeController Controller = new();
    public readonly FakeMotion Motion = new();
    public readonly FakeDevice Device;
    public readonly FakeRemote Remote;
    public readonly CancellationTokenSource Cancel = new();
    public readonly SessionManager Session;
    public Fixture()
    {
        Device = new(Events); Remote = new(Events);
        Session = new(new() { RestartGraceSeconds = 0, EnableRumble = true }, Controller, Motion, Device, Remote, new FakeTouch(), _ => { });
    }
    public async Task WaitForInput()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!Device.States.Any(s => s.Buttons.HasFlag(DS4Buttons.Cross)))
        { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(5); }
    }
    public void Dispose() { Cancel.Cancel(); Cancel.Dispose(); }
}
sealed class FakeController : IController
{
    public volatile bool Connected = true;
    public int RumbleStops, RumbleCalls;
    public void Prepare() { }
    public bool TryRead(out ControllerState state) { state = new(XboxButtons.A, 0, 0, 0, 0, 0, 0); return Connected; }
    public void Rumble(byte small, byte large) { Interlocked.Increment(ref RumbleCalls); if (small == 0 && large == 0) Interlocked.Increment(ref RumbleStops); }
}
sealed class FakeMotion : IMotionSource
{
    public string Status => "Fake motion";
    public volatile bool Stopped;
    public int Starts;
    public void Start() { Interlocked.Increment(ref Starts); Stopped = false; }
    public bool TryRead(out MotionSample sample) { sample = default; return false; }
    public void Stop() => Stopped = true;
    public void Dispose() => Stop();
}
sealed class FakeDevice(List<string> events) : IVirtualController
{
    public event Action<byte, byte>? RumbleReceived;
    public void EmitRumble(byte small, byte large) => RumbleReceived?.Invoke(small, large);
    public readonly ConcurrentQueue<DS4State> States = new();
    public bool Detached, FailAttach, FailSend;
    public Task AttachAsync(CancellationToken token) { events.Add("attach"); if (FailAttach) throw new IOException("Attach failed"); return Task.CompletedTask; }
    public void Send(DS4State state) { if (FailSend) throw new IOException("Send failed"); States.Enqueue(state); }
    public void Detach() => Detached = true;
    public void Dispose() => Detach();
}
sealed class FakeRemote(List<string> events) : IRemotePlay
{
    public volatile bool Running = true, Foreground = true;
    public Task PrepareAsync(CancellationToken token) => Task.CompletedTask;
    public Task LaunchAsync(CancellationToken token) { events.Add("launch"); return Task.CompletedTask; }
    public bool IsRunning => Running;
    public bool IsForeground => Foreground;
    public void Dispose() { }
}
sealed class FakeTouch : ITouchSurface
{
    public TouchState Read() => default;
    public void Toggle() { }
    public void Deactivate() { }
}
