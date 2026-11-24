using System.Numerics;
using Windows.Devices.Sensors;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

// Windows exposes Sensor HID/Bosch devices through the sensor stack when supported by their driver.
// Raw Bosch reports must never be guessed: a verified model-specific adapter can implement IMotionSource.
public sealed class SensorReader : IMotionSource
{
    private readonly object readingsLock = new();
    private (Vector3 Value, DateTimeOffset Timestamp)? gyroReading, accelerationReading;
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
        gyro.ReadingChanged += OnGyroReading;
        accelerometer.ReadingChanged += OnAccelerationReading;
        Status = "Windows gyro and accelerometer available; verify axis orientation on hardware";
    }
    private void OnGyroReading(Gyrometer sender, GyrometerReadingChangedEventArgs args)
    {
        var reading = args.Reading;
        var sample = (new Vector3((float)reading.AngularVelocityX, (float)reading.AngularVelocityY, (float)reading.AngularVelocityZ), reading.Timestamp);
        lock (readingsLock)
            if (ReferenceEquals(gyro, sender)) gyroReading = sample;
    }

    private void OnAccelerationReading(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
    {
        var reading = args.Reading;
        var sample = (new Vector3((float)reading.AccelerationX, (float)reading.AccelerationY, (float)reading.AccelerationZ) * 9.81f, reading.Timestamp);
        lock (readingsLock)
            if (ReferenceEquals(accelerometer, sender)) accelerationReading = sample;
    }

    public bool TryRead(out MotionSample sample)
    {
        lock (readingsLock)
        {
            if (gyroReading is { } g && accelerationReading is { } a)
            {
                sample = new(g.Value, a.Value, g.Timestamp < a.Timestamp ? g.Timestamp : a.Timestamp);
                return true;
            }
        }
        sample = default;
        return false;
    }

    public void Stop()
    {
        Gyrometer? previousGyro;
        Accelerometer? previousAccelerometer;
        lock (readingsLock)
        {
            previousGyro = gyro;
            previousAccelerometer = accelerometer;
            gyro = null;
            accelerometer = null;
            gyroReading = null;
            accelerationReading = null;
        }
        try
        {
            if (previousGyro is not null)
            {
                previousGyro.ReadingChanged -= OnGyroReading;
                previousGyro.ReportInterval = 0;
            }
        }
        finally
        {
            if (previousAccelerometer is not null)
            {
                previousAccelerometer.ReadingChanged -= OnAccelerationReading;
                previousAccelerometer.ReportInterval = 0;
            }
        }
    }

    public async Task<float[]> CalibrateAsync(CancellationToken cancellationToken)
    {
        Start();
        try
        {
            if (gyro is null || accelerometer is null) throw new InvalidOperationException(Status);
            var startup = System.Diagnostics.Stopwatch.StartNew();
            while (!TryRead(out _))
            {
                if (startup.Elapsed >= TimeSpan.FromSeconds(2))
                    throw new InvalidOperationException("No motion samples received.");
                await Task.Delay(10, cancellationToken);
            }

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
