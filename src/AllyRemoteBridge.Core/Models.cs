using System.Numerics;

namespace AllyRemoteBridge.Core;

[Flags]
public enum XboxButtons : ushort
{
    None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Menu = 16, View = 32,
    LS = 64, RS = 128, LB = 256, RB = 512, A = 4096, B = 8192, X = 16384, Y = 32768
}
[Flags]
public enum DS4Buttons : ushort
{
    None = 0, PS = 1, Touchpad = 2, Square = 16, Cross = 32, Circle = 64,
    Triangle = 128, L1 = 256, R1 = 512, L2 = 1024, R2 = 2048,
    Share = 4096, Options = 8192, L3 = 16384, R3 = 32768
}
public readonly record struct ControllerState(XboxButtons Buttons, byte LT, byte RT, short LX, short LY, short RX, short RY);
public readonly record struct TouchContact(ushort X, ushort Y, bool Active);
public readonly record struct TouchState(TouchContact First, TouchContact Second);
public readonly record struct MotionSample(Vector3 GyroDps, Vector3 AccelerationMS2, DateTimeOffset Timestamp);
public readonly record struct MotionState(short GX, short GY, short GZ, short AX, short AY, short AZ)
{
    public static MotionState Neutral => new(0, 0, 0, 0, 0, -8192);
}
public readonly record struct DS4State(sbyte LX, sbyte LY, sbyte RX, sbyte RY,
    DS4Buttons Buttons, byte DPad, byte L2, byte R2, TouchState Touch, MotionState Motion)
{
    public static DS4State Neutral => new(0, 0, 0, 0, 0, 0, 0, 0, default, MotionState.Neutral);
}
public enum SessionState { Idle, Preparing, Attaching, Launching, Active, Stopping }

public interface IController
{
    void Prepare();
    bool TryRead(out ControllerState state);
    void Rumble(byte small, byte large);
}
public interface IMotionSource : IDisposable
{
    string Status { get; }
    void Start();
    bool TryRead(out MotionSample sample);
    void Stop();
}
public interface IVirtualController : IDisposable
{
    event Action<byte, byte>? RumbleReceived;
    Task AttachAsync(CancellationToken cancellationToken);
    void Send(DS4State state);
    void Detach();
}
public interface IRemotePlay : IDisposable
{
    Task PrepareAsync(CancellationToken cancellationToken);
    Task LaunchAsync(CancellationToken cancellationToken);
    bool IsRunning { get; }
    bool IsForeground { get; }
}
public interface ITouchSurface
{
    TouchState Read();
    void Toggle();
    void Deactivate();
}
