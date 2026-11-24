using AllyRemoteBridge.Core;
using AllyRemoteBridge.Devices;

namespace AllyRemoteBridge.App;

internal sealed class SettingsWindow : Form
{
    private BridgeSettings settings;
    private readonly TextBox remotePath = new() { Width = 390 };
    private readonly ComboBox controller = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly NumericUpDown deadZone = Number(0, 99), grace = Number(0, 120);
    private readonly CheckBox gyro = Check("Enable gyro and accelerometer"), touch = Check("Enable on-screen touchpad"), rumble = Check("Forward vibration"), launch = Check("Automatically launch PS Remote Play");
    private readonly CheckBox fullScreenTouch = Check("Automatically use the whole Remote Play screen");
    private readonly ComboBox prefix = ButtonChoice(), click = ButtonChoice(), ps = ButtonChoice(), overlay = ButtonChoice();
    private readonly NumericUpDown x = Number(-10000, 10000), y = Number(-10000, 10000), width = Number(100, 3840), height = Number(80, 2160);
    private readonly NumericUpDown[] axes = [Number(-3, 3), Number(-3, 3), Number(-3, 3)];
    private readonly NumericUpDown smoothing = Number(1, 100);
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(600, 0) };
    private readonly CancellationTokenSource cancellation = new();
    public SettingsWindow(BridgeSettings settings, bool sessionActive)
    {
        this.settings = settings;
        Text = "Ally Remote Bridge — Settings"; Size = new(730, 760); MinimumSize = new(600, 500);
        StartPosition = FormStartPosition.CenterScreen;
        var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Padding = new(16), AutoSizeMode = AutoSizeMode.GrowAndShrink };
        rows.ColumnStyles.Add(new(SizeType.Absolute, 230)); rows.ColumnStyles.Add(new(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(rows); Controls.Add(scroll);
        void Add(string title, Control value)
        {
            int row = rows.RowCount++;
            rows.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new(3, 8, 3, 8) }, 0, row);
            value.Margin = new(3, 6, 3, 6); rows.Controls.Add(value, 1, row);
        }
        remotePath.Text = settings.RemotePlayPath ?? "";
        var pathPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => { using var picker = new OpenFileDialog { Filter = "PS Remote Play|RemotePlay.exe", CheckFileExists = true }; if (picker.ShowDialog(this) == DialogResult.OK) remotePath.Text = picker.FileName; };
        pathPanel.Controls.Add(remotePath); pathPanel.Controls.Add(browse); Add("PS Remote Play (blank = detect)", pathPanel);
        controller.Items.AddRange(["Auto: only connected controller", "XInput slot 0", "XInput slot 1", "XInput slot 2", "XInput slot 3"]);
        controller.SelectedIndex = settings.ControllerIndex is { } slot ? (int)slot + 1 : 0;
        Add("Physical controller", controller);
        Add("Connected XInput slots", new Label { Text = string.Join(", ", XInputController.ConnectedSlots()), AutoSize = true });
        deadZone.Value = (decimal)(settings.DeadZone * 100); Add("Stick dead zone (%)", deadZone);
        gyro.Checked = settings.EnableMotion; touch.Checked = settings.EnableTouchpad; rumble.Checked = settings.EnableRumble; launch.Checked = settings.AutoLaunch;
        fullScreenTouch.Checked = settings.FullScreenTouchpad;
        fullScreenTouch.Enabled = touch.Checked;
        touch.CheckedChanged += (_, _) => fullScreenTouch.Enabled = touch.Checked;
        Add("Motion", gyro); Add("Touchpad", touch); Add("Full-screen touchpad", fullScreenTouch);
        Add("Vibration (optional)", rumble); Add("Startup", launch);
        prefix.SelectedItem = settings.ShortcutPrefix; click.SelectedItem = settings.TouchClickButton; ps.SelectedItem = settings.PSButton; overlay.SelectedItem = settings.OverlayButton;
        Add("Shortcut prefix (press first)", prefix); Add("Touchpad click: prefix +", click); Add("PS: prefix +", ps); Add("Toggle touchpad: prefix +", overlay);
        grace.Value = settings.RestartGraceSeconds;
        Add("Sony restart grace (seconds)", grace);
        x.Value = Math.Clamp(settings.OverlayX, -10000, 10000); y.Value = Math.Clamp(settings.OverlayY, -10000, 10000);
        width.Value = settings.OverlayWidth; height.Value = settings.OverlayHeight;
        Add("Touchpad screen X", x); Add("Touchpad screen Y", y); Add("Touchpad width", width); Add("Touchpad height", height);
        for (int i = 0; i < 3; i++) { axes[i].Value = settings.MotionAxes[i]; Add($"DS4 {"XYZ"[i]} axis (±1=X, ±2=Y, ±3=Z)", axes[i]); }
        smoothing.Value = (decimal)(settings.MotionSmoothing * 100); Add("Motion response (1–100%)", smoothing);
        var calibrate = new Button { Text = "Calibrate gyro (keep handheld still)", AutoSize = true, Enabled = !sessionActive };
        calibrate.Click += async (_, _) =>
        {
            calibrate.Enabled = false;
            try
            {
                status.Text = "Keep the Ally still for one second…";
                using var sensor = new SensorReader();
                var bias = await sensor.CalibrateAsync(cancellation.Token);
                this.settings = this.settings with { GyroBias = bias };
                status.Text = "Gyro bias calibrated. Save to keep it. Verify axis orientation separately in a motion-enabled game.";
            }
            catch (Exception e) { status.Text = e.Message; }
            finally { if (!IsDisposed) calibrate.Enabled = true; }
        };
        Add("Gyro calibration", calibrate);
        status.Text = sessionActive ? "Changes apply next launch. Stop the session before calibrating gyro." : "A virtual DS4 is globally visible while a session is active. Motion axis orientation requires hardware verification.";
        Add("", status);
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += (_, _) => Save(); Add("", save);
    }
    private static CheckBox Check(string title) => new() { Text = title, AutoSize = true };
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 110 };
    private static ComboBox ButtonChoice()
    {
        var result = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        foreach (var b in Enum.GetValues<XboxButtons>().Where(b => b != XboxButtons.None)) result.Items.Add(b);
        return result;
    }
    private void Save()
    {
        try
        {
            var value = settings with
            {
                RemotePlayPath = string.IsNullOrWhiteSpace(remotePath.Text) ? null : remotePath.Text.Trim(),
                ControllerIndex = controller.SelectedIndex == 0 ? null : (uint)(controller.SelectedIndex - 1),
                DeadZone = (double)deadZone.Value / 100, EnableMotion = gyro.Checked, EnableTouchpad = touch.Checked,
                FullScreenTouchpad = fullScreenTouch.Checked,
                EnableRumble = rumble.Checked, AutoLaunch = launch.Checked, RestartGraceSeconds = (int)grace.Value,
                ShortcutPrefix = (XboxButtons)prefix.SelectedItem!, TouchClickButton = (XboxButtons)click.SelectedItem!, PSButton = (XboxButtons)ps.SelectedItem!, OverlayButton = (XboxButtons)overlay.SelectedItem!,
                OverlayX = (int)x.Value, OverlayY = (int)y.Value, OverlayWidth = (int)width.Value, OverlayHeight = (int)height.Value,
                MotionAxes = axes.Select(a => (int)a.Value).ToArray(), MotionSmoothing = (double)smoothing.Value / 100
            };
            SettingsStore.Save(value); Close();
        }
        catch (Exception e) { status.Text = e.Message; }
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { cancellation.Cancel(); cancellation.Dispose(); base.OnFormClosed(e); }
}
