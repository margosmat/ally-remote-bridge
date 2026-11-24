using System.Diagnostics;

namespace AllyRemoteBridge.Core;

public sealed class SessionManager(BridgeSettings settings, IController controller, IMotionSource motion,
    IVirtualController device, IRemotePlay remote, ITouchSurface touch, Action<string> log)
{
    private int running, resumeVersion;
    private volatile bool suspended, forwarding;
    private RumbleManager? activeRumble;
    public bool IsForwarding => forwarding;
    public SessionState State { get; private set; }
    public void Suspend(bool value)
    {
        bool previous = suspended;
        suspended = value;
        if (value) { forwarding = false; activeRumble?.SetActive(false); }
        else if (previous) Interlocked.Increment(ref resumeVersion);
    }
    public event Action<SessionState>? StateChanged;
    private void Transition(SessionState state) { State = state; log($"Session: {state}"); StateChanged?.Invoke(state); }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Session is already running.");
        var mapper = new DS4Mapper(settings);
        var motionMapper = new MotionManager(settings);
        var rumble = new RumbleManager(settings.EnableRumble, controller, log);
        activeRumble = rumble;
        void Rumble(byte small, byte large) { if (forwarding) rumble.Forward(small, large); }
        try
        {
            Transition(SessionState.Preparing);
            settings.Validate();
            controller.Prepare();
            await remote.PrepareAsync(cancellationToken);
            Transition(SessionState.Attaching);
            await device.AttachAsync(cancellationToken);
            device.RumbleReceived += Rumble;
            device.Send(DS4State.Neutral);
            Transition(SessionState.Launching);
            await remote.LaunchAsync(cancellationToken);
            if (settings.EnableMotion)
            {
                try { motion.Start(); log(motion.Status); }
                catch (Exception e) { log($"Motion unavailable: {e.GetType().Name}"); }
            }
            Transition(SessionState.Active);
            var clock = Stopwatch.StartNew();
            TimeSpan? missingSince = null;
            DS4State? last = null;
            bool wasForwarding = false, sensorPaused = false;
            string? lastInputStatus = null;
            TimeSpan timingStart = clock.Elapsed;
            TimeSpan? previousRead = null;
            double maxReadGapMs = 0, maxSendMs = 0;
            int readCount = 0, sendCount = 0;
            XboxButtons lastStickClicks = XboxButtons.None;
            int observedResume = Volatile.Read(ref resumeVersion);
            touch.Deactivate();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!remote.IsRunning)
                {
                    missingSince ??= clock.Elapsed;
                    if (clock.Elapsed - missingSince.Value >= TimeSpan.FromSeconds(settings.RestartGraceSeconds)) break;
                }
                else missingSince = null;
                if (suspended && !sensorPaused)
                {
                    if (settings.EnableMotion) motion.Stop();
                    sensorPaused = true;
                }
                int currentResume = Volatile.Read(ref resumeVersion);
                if (!suspended && (sensorPaused || currentResume != observedResume))
                {
                    forwarding = false;
                    rumble.SetActive(false);
                    touch.Deactivate(); mapper.Reset(); motionMapper.Reset();
                    device.Detach();
                    await device.AttachAsync(cancellationToken);
                    device.Send(DS4State.Neutral); last = null;
                    if (settings.EnableMotion)
                    {
                        try { motion.Stop(); motion.Start(); }
                        catch (Exception e) { log($"Motion resume failed: {e.GetType().Name}"); }
                    }
                    sensorPaused = false;
                    observedResume = currentResume;
                }
                string inputStatus = suspended ? "paused while Windows is suspended"
                    : missingSince is not null ? "paused because PS Remote Play is not running"
                    : !remote.IsForeground ? "paused because PS Remote Play is not the foreground window"
                    : "forwarding";
                forwarding = inputStatus == "forwarding";
                DS4State next = DS4State.Neutral;
                if (forwarding && controller.TryRead(out var input))
                {
                    TimeSpan readAt = clock.Elapsed;
                    if (previousRead is { } previous)
                        maxReadGapMs = Math.Max(maxReadGapMs, (readAt - previous).TotalMilliseconds);
                    previousRead = readAt;
                    readCount++;
                    MotionState m = MotionState.Neutral;
                    if (settings.EnableMotion && motion.TryRead(out var sample)) m = motionMapper.Convert(sample, DateTimeOffset.UtcNow);
                    next = mapper.Map(input, clock.Elapsed, settings.EnableTouchpad ? touch.Read() : default, m);
                    if (next.Buttons.HasFlag(DS4Buttons.PS) && last?.Buttons.HasFlag(DS4Buttons.PS) != true)
                        log(input.PlayStationPressed ? "PS pressed via ASUS button or keyboard shortcut" : "PS pressed via controller shortcut");
                    var stickClicks = input.Buttons & (XboxButtons.LS | XboxButtons.RS);
                    if (stickClicks != lastStickClicks)
                    {
                        log($"Stick clicks: XInput {stickClicks}, DS4 {next.Buttons & (DS4Buttons.L3 | DS4Buttons.R3)}");
                        lastStickClicks = stickClicks;
                    }
                    if (settings.EnableTouchpad && mapper.OverlayRequested) touch.Toggle();
                }
                else
                {
                    previousRead = null;
                    if (forwarding) inputStatus = "paused because the selected XInput slot is unavailable";
                    forwarding = false;
                    mapper.Reset(); motionMapper.Reset();
                    if (wasForwarding) touch.Deactivate();
                }
                if (inputStatus != lastInputStatus)
                {
                    log($"Controller input: {inputStatus}");
                    lastInputStatus = inputStatus;
                }
                rumble.SetActive(forwarding);
                wasForwarding = forwarding;
                if (last != next)
                {
                    long sendStart = Stopwatch.GetTimestamp();
                    device.Send(next);
                    maxSendMs = Math.Max(maxSendMs, Stopwatch.GetElapsedTime(sendStart).TotalMilliseconds);
                    sendCount++;
                    last = next;
                }
                if (clock.Elapsed - timingStart >= TimeSpan.FromSeconds(5))
                {
                    if (readCount > 0)
                        log($"Input timing: {readCount} XInput reads, max read gap {maxReadGapMs:F1} ms, {sendCount} DS4 reports, max send call {maxSendMs:F1} ms");
                    timingStart = clock.Elapsed;
                    maxReadGapMs = maxSendMs = 0;
                    readCount = sendCount = 0;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            forwarding = false;
            Transition(SessionState.Stopping);
            device.RumbleReceived -= Rumble;
            List<Exception> cleanupErrors = [];
            // Every cleanup step must run even if an earlier operation fails.
            void Clean(Action action) { try { action(); } catch (Exception e) { cleanupErrors.Add(e); log($"Cleanup failed: {e.GetType().Name}: {e.Message}"); } }
            Clean(touch.Deactivate);
            Clean(motion.Stop);
            Clean(() => rumble.SetActive(false));
            activeRumble = null;
            Clean(() => device.Send(DS4State.Neutral));
            Clean(device.Detach);
            Interlocked.Exchange(ref running, 0);
            Transition(SessionState.Idle);
            if (cleanupErrors.Count != 0) throw new AggregateException("Cleanup failed. Run the recovery shortcut before another session.", cleanupErrors);
        }
    }
}
