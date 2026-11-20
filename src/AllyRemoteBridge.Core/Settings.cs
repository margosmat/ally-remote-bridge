using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllyRemoteBridge.Core;

public sealed record BridgeSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? RemotePlayPath { get; init; }
    public uint? ControllerIndex { get; init; }
    public double DeadZone { get; init; } = .08;
    public bool EnableMotion { get; init; } = true;
    public bool EnableTouchpad { get; init; } = true;
    public bool EnableRumble { get; init; }
    public bool AutoLaunch { get; init; } = true;
    public int RestartGraceSeconds { get; init; } = 10;
    public XboxButtons ShortcutPrefix { get; init; } = XboxButtons.View;
    public XboxButtons TouchClickButton { get; init; } = XboxButtons.X;
    public XboxButtons PSButton { get; init; } = XboxButtons.Menu;
    public XboxButtons OverlayButton { get; init; } = XboxButtons.Y;
    public int PSHoldMilliseconds { get; init; } = 600;
    public int OverlayX { get; init; } = 80;
    public int OverlayY { get; init; } = 80;
    public int OverlayWidth { get; init; } = 480;
    public int OverlayHeight { get; init; } = 236;
    public double MotionSmoothing { get; init; } = .65;
    // Signed axis indices: +1 = X, -2 = inverted Y. Hardware orientation requires calibration.
    public int[] MotionAxes { get; init; } = [1, 2, 3];
    public float[] GyroBias { get; init; } = [0, 0, 0];

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings schema.");
        if (!double.IsFinite(DeadZone) || DeadZone is < 0 or >= 1) throw new InvalidDataException("Dead zone must be between 0 and 0.99.");
        if (ControllerIndex > 3) throw new InvalidDataException("Controller slot must be 0–3.");
        if (RestartGraceSeconds is < 0 or > 120 || PSHoldMilliseconds is < 200 or > 5000) throw new InvalidDataException("Invalid session or shortcut timing.");
        if (OverlayWidth is < 100 or > 3840 || OverlayHeight is < 80 or > 2160) throw new InvalidDataException("Invalid touchpad dimensions.");
        if (!double.IsFinite(MotionSmoothing) || MotionSmoothing is <= 0 or > 1) throw new InvalidDataException("Motion smoothing must be greater than 0 and at most 1.");
        if (MotionAxes is not { Length: 3 } || MotionAxes.Any(x => x is < -3 or > 3 || x == 0) || MotionAxes.Select(Math.Abs).Distinct().Count() != 3)
            throw new InvalidDataException("Motion axes must be a signed permutation of 1, 2, 3.");
        if (GyroBias is not { Length: 3 } || GyroBias.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Invalid gyro calibration.");
        XboxButtons[] buttons = [ShortcutPrefix, TouchClickButton, PSButton, OverlayButton];
        if (buttons.Distinct().Count() != 4 || buttons.Any(b => (ushort)b == 0 || (((ushort)b & ((ushort)b - 1)) != 0) || !Enum.IsDefined(b)))
            throw new InvalidDataException("Shortcuts require four distinct individual buttons.");
        if (RemotePlayPath is not null && !Path.IsPathFullyQualified(RemotePlayPath)) throw new InvalidDataException("Remote Play path must be absolute.");
    }
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllyRemoteBridge");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static BridgeSettings Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path)) return new();
        var result = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty settings.");
        result.Validate();
        return result;
    }
    public static void Save(BridgeSettings settings, string? path = null)
    {
        settings.Validate();
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, path, true);
    }
}
