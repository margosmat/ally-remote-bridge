using System.Numerics;
using Windows.Devices.Sensors;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

// Windows exposes Sensor HID/Bosch devices through the sensor stack when supported by their driver.
// Raw Bosch reports must never be guessed: a verified model-specific adapter can implement IMotionSource.
public sealed class SensorReader : IMotionSource
{
    private Gyrometer? gyro;
    private Accelerometer? accelerometer;
    public string Status { get; private set; } = "Motion inactive";
    public void Start()
    {
        Stop();
        gyro = Gyrometer.GetDefault();
        accelerometer = Accelerometer.GetDefault();
        if (gyro is null || accelerometer is null)
        {
            Stop();
            Status = "Gyro unavailable: install the ASUS sensor driver. Raw Bosch HID support requires hardware validation.";
            return;
        }
        gyro.ReportInterval = Math.Max(4u, gyro.MinimumReportInterval);
        accelerometer.ReportInterval = Math.Max(4u, accelerometer.MinimumReportInterval);
        Status = "Windows gyro and accelerometer available; verify axis orientation on hardware";
    }
    public bool TryRead(out MotionSample sample)
    {
        sample = default;
        if (gyro is null || accelerometer is null) return false;
        try
        {
            var g = gyro.GetCurrentReading();
            var a = accelerometer.GetCurrentReading();
            if (g is null || a is null) return false;
            var timestamp = g.Timestamp < a.Timestamp ? g.Timestamp : a.Timestamp;
            sample = new(new((float)g.AngularVelocityX, (float)g.AngularVelocityY, (float)g.AngularVelocityZ),
                new Vector3((float)a.AccelerationX, (float)a.AccelerationY, (float)a.AccelerationZ) * 9.81f, timestamp);
            return true;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { Status = "Motion sensor disconnected or access denied"; Stop(); return false; }
    }
    public void Stop()
    {
        try { if (gyro is not null) gyro.ReportInterval = 0; }
        finally
        {
            gyro = null;
            try { if (accelerometer is not null) accelerometer.ReportInterval = 0; }
            finally { accelerometer = null; }
        }
    }
    public async Task<float[]> CalibrateAsync(CancellationToken cancellationToken)
    {
        Start();
        try
        {
            Vector3 sum = default;
            int count = 0;
            for (int i = 0; i < 200; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryRead(out var sample)) throw new InvalidOperationException(Status);
                if (sample.GyroDps.Length() > 10 || Math.Abs(sample.AccelerationMS2.Length() - 9.81) > 2)
                    throw new InvalidOperationException("Keep the handheld still during calibration.");
                sum += sample.GyroDps; count++;
                await Task.Delay(5, cancellationToken);
            }
            var bias = sum / count;
            return [bias.X, bias.Y, bias.Z];
        }
        finally { Stop(); }
    }
    public void Dispose() => Stop();
}
