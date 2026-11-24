using System.Runtime.InteropServices;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Overlay;

public sealed class TouchpadWindow : Form, ITouchSurface
{
    private readonly TouchManager touches = new();
    private bool active;
    private readonly bool fullScreen;
    private bool fullScreenPaused;
    private readonly string hint;
    public TouchpadWindow(BridgeSettings settings)
    {
        fullScreen = settings.FullScreenTouchpad;
        hint = $"DS4 TOUCHPAD · {settings.ShortcutPrefix} + {settings.OverlayButton} to close";
        Text = "Ally Remote Bridge touchpad";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new(settings.OverlayX, settings.OverlayY, settings.OverlayWidth, settings.OverlayHeight);
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(Bounds))) Location = Screen.PrimaryScreen!.WorkingArea.Location;
        BackColor = Color.FromArgb(24, 35, 53); ForeColor = Color.White;
        Opacity = fullScreen ? .01 : .55; TopMost = true; ShowInTaskbar = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        // Keep the handle alive for worker-thread dispatch without displaying the overlay.
        _ = Handle;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } // NOACTIVATE | TOOLWINDOW
    }
    private void Dispatch(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
    public TouchState Read() => touches.Read();
    public void Toggle() => Dispatch(() =>
    {
        if (fullScreen)
        {
            fullScreenPaused = !fullScreenPaused;
            UpdateSession(true);
            return;
        }
        active = !active;
        touches.Reset();
        if (active) Show(); else Hide();
    });
    public void UpdateSession(bool forwarding)
    {
        if (!forwarding || fullScreenPaused)
        {
            if (active) Deactivate();
            return;
        }
        if (!fullScreen) return;
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return;
        var screenBounds = Screen.FromHandle(foreground).Bounds;
        if (Bounds != screenBounds)
        {
            touches.Reset();
            Bounds = screenBounds;
        }
        if (!active) { active = true; Show(); }
    }
    public new void Deactivate() { touches.Reset(); Dispatch(() => { if (active) { active = false; Hide(); } }); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (fullScreen) return;
        e.Graphics.DrawRectangle(Pens.LightSlateGray, 1, 1, Width - 3, Height - 3);
        TextRenderer.DrawText(e.Graphics, hint, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void WndProc(ref Message m)
    {
        const int PointerUpdate = 0x245, PointerDown = 0x246, PointerUp = 0x247, CaptureChanged = 0x24c;
        if (m.Msg == 0x21) { m.Result = 3; return; } // MA_NOACTIVATE
        if (m.Msg is PointerDown or PointerUpdate or PointerUp or CaptureChanged)
        {
            uint id = (uint)((long)m.WParam & 0xffff);
            if (m.Msg is PointerUp or CaptureChanged) touches.Release(id, canceled: m.Msg == CaptureChanged);
            else if (active && GetPointerInfo(id, out var info) && info.Type == 2)
            {
                if ((info.Flags & 0x8000) != 0) touches.Release(id, canceled: true);
                else if ((info.Flags & 4) != 0) // POINTER_FLAG_INCONTACT
                {
                    var point = PointToClient(new(info.Location.X, info.Location.Y));
                    touches.Update(id, point.X, point.Y, ClientSize.Width, ClientSize.Height);
                }
            }
            m.Result = 0; return;
        }
        base.WndProc(ref m);
    }
    protected override void OnVisibleChanged(EventArgs e) { if (!Visible) touches.Reset(); base.OnVisibleChanged(e); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct PointerInfo
    {
        public uint Type, Id, FrameId, Flags;
        public nint Source, Target;
        public NativePoint Location, Himetric, RawLocation, RawHimetric;
        public uint Time, History;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChange;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPointerInfo(uint id, out PointerInfo info);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
