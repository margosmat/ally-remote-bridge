using System.Numerics;

namespace AllyRemoteBridge.Core;

public sealed class MotionManager(BridgeSettings settings)
{
    private Vector3 gyro, acceleration;
    private bool initialized;
    public void Reset() => initialized = false;
    public MotionState Convert(MotionSample sample, DateTimeOffset now)
    {
        if (now - sample.Timestamp > TimeSpan.FromMilliseconds(100) || sample.Timestamp > now + TimeSpan.FromMilliseconds(100))
        { Reset(); return MotionState.Neutral; }
        var g = Rotate(sample.GyroDps - new Vector3(settings.GyroBias[0], settings.GyroBias[1], settings.GyroBias[2]));
        var a = Rotate(sample.AccelerationMS2);
        if (!Finite(g) || !Finite(a)) { Reset(); return MotionState.Neutral; }
        float alpha = (float)settings.MotionSmoothing;
        gyro = initialized ? Vector3.Lerp(gyro, g, alpha) : g;
        acceleration = initialized ? Vector3.Lerp(acceleration, a, alpha) : a;
        initialized = true;
        static short Quantize(float value, float scale) => (short)Math.Clamp(MathF.Round(value * scale), short.MinValue, short.MaxValue);
        return new(Quantize(gyro.X, 16), Quantize(gyro.Y, 16), Quantize(gyro.Z, 16),
            Quantize(acceleration.X, 835.07f), Quantize(acceleration.Y, 835.07f), Quantize(acceleration.Z, 835.07f));
    }
    private Vector3 Rotate(Vector3 value)
    {
        float Axis(int index) => Math.Sign(index) * (Math.Abs(index) switch { 1 => value.X, 2 => value.Y, _ => value.Z });
        return new(Axis(settings.MotionAxes[0]), Axis(settings.MotionAxes[1]), Axis(settings.MotionAxes[2]));
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
