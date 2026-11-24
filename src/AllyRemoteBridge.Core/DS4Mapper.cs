namespace AllyRemoteBridge.Core;

public sealed class DS4Mapper(BridgeSettings settings)
{
    private bool prefixHeld, consumed, overlaySent, sharePulse;
    private bool shareHeld;
    private TimeSpan prefixStarted;
    private XboxButtons suppressed;
    private TimeSpan? psStart;
    public bool OverlayRequested { get; private set; }

    public void Reset()
    {
        prefixHeld = consumed = overlaySent = sharePulse = shareHeld = false;
        suppressed = 0; psStart = null;
        OverlayRequested = false;
    }

    public DS4State Map(ControllerState input, TimeSpan now, TouchState touch, MotionState motion)
    {
        OverlayRequested = false;
        var buttons = input.Buttons;
        DS4Buttons extra = input.PlayStationPressed ? DS4Buttons.PS : DS4Buttons.None;
        // Give shortcut presses time to claim the prefix before forwarding a standalone hold.
        bool prefix = buttons.HasFlag(settings.ShortcutPrefix);
        if (prefix && !prefixHeld)
        {
            consumed = overlaySent = shareHeld = false;
            prefixStarted = now;
        }
        if (prefix && !shareHeld)
        {
            if (buttons.HasFlag(settings.TouchClickButton)) { extra |= DS4Buttons.Touchpad; consumed = true; suppressed |= settings.TouchClickButton; }
            if (buttons.HasFlag(settings.PSButton))
            {
                consumed = true;
                suppressed |= settings.PSButton;
                psStart ??= now;
                if (now - psStart.Value >= TimeSpan.FromMilliseconds(settings.PSHoldMilliseconds)) extra |= DS4Buttons.PS;
            }
            else psStart = null;
            if (buttons.HasFlag(settings.OverlayButton))
            {
                consumed = true;
                suppressed |= settings.OverlayButton;
                if (!overlaySent) { OverlayRequested = true; overlaySent = true; }
            }
        }
        if (!prefix) psStart = null;
        if (prefix && !consumed && now - prefixStarted >= TimeSpan.FromMilliseconds(250)) shareHeld = true;
        if (!prefix && prefixHeld && !consumed && !shareHeld) sharePulse = true;
        if (sharePulse || (prefix && shareHeld)) { buttons |= settings.ShortcutPrefix; sharePulse = false; }
        else buttons &= ~settings.ShortcutPrefix;
        prefixHeld = prefix;
        buttons &= ~suppressed;
        suppressed &= input.Buttons; // Keep consumed buttons suppressed until physically released.
        var result = extra;
        foreach (var (from, to) in ButtonMap) if (buttons.HasFlag(from)) result |= to;
        if (input.LT > 0) result |= DS4Buttons.L2;
        if (input.RT > 0) result |= DS4Buttons.R2;
        // libVIIPER accepts direction bits, NOT the DS4 USB hat encoding.
        byte dpad = (byte)((ushort)buttons & 15);
        if ((dpad & 3) == 3) dpad &= 12;
        if ((dpad & 12) == 12) dpad &= 3;
        var (lx, ly) = Stick(input.LX, input.LY);
        var (rx, ry) = Stick(input.RX, input.RY);
        return new(lx, ly, rx, ry, result, dpad, input.LT, input.RT, touch, motion);
    }

    private static readonly (XboxButtons From, DS4Buttons To)[] ButtonMap = [
            (XboxButtons.A, DS4Buttons.Cross), (XboxButtons.B, DS4Buttons.Circle),
            (XboxButtons.X, DS4Buttons.Square), (XboxButtons.Y, DS4Buttons.Triangle),
            (XboxButtons.LB, DS4Buttons.L1), (XboxButtons.RB, DS4Buttons.R1),
            (XboxButtons.LS, DS4Buttons.L3), (XboxButtons.RS, DS4Buttons.R3),
            (XboxButtons.View, DS4Buttons.Share), (XboxButtons.Menu, DS4Buttons.Options)];
    private (sbyte, sbyte) Stick(short x, short y)
    {
        double nx = x / (x < 0 ? 32768d : 32767d), ny = y / (y < 0 ? 32768d : 32767d);
        double length = Math.Sqrt(nx * nx + ny * ny);
        if (length <= settings.DeadZone) return (0, 0);
        double scale = Math.Min(1, (length - settings.DeadZone) / (1 - settings.DeadZone)) / length;
        static sbyte Axis(double value) => (sbyte)Math.Clamp(Math.Round(value * (value < 0 ? 128 : 127)), -128, 127);
        return (Axis(nx * scale), Axis(-ny * scale));
    }
}
