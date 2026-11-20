# Project Plan: Ally Remote Bridge

Specification v1.0

Platform: Windows 11 • ASUS ROG Xbox Ally X • PS5

## 1. Application goal

Build a small application for the ASUS ROG Xbox Ally X that allows the official PS Remote Play application to use the handheld's built-in controller, touchscreen, and motion sensors.

The application must run in the background without replacing the system Xbox controller or ASUS software.

Target user experience:

1. I launch `AllyRemoteBridge.exe`.
2. The program prepares a virtual DualShock 4.
3. It automatically launches official PS Remote Play.
4. I play on my PS5 using the Ally X controller, touchpad, and gyro.
5. I close Remote Play; the program automatically removes the virtual controller and exits.
6. Other games work as before, without restoring any configuration.

We are not building an alternative Remote Play client. PlayStation Network sign-in, connecting to the PS5, and video streaming remain the responsibility of Sony's application.

## 2. Functional scope

| Feature | Requirement |
| --- | --- |
| DualShock 4 emulation | Full buttons, sticks, and triggers |
| Touchpad | Screen touch, swipes, and clicking |
| Motion controls | Ally X gyroscope and accelerometer |
| Rumble | Forward to the handheld's motors where possible |
| PS button | Configurable button combination |
| Start/stop | Automatic alongside PS Remote Play |
| Background operation | No visible console window |
| Configuration | Simple settings, ready to use by default |
| Other games | No hiding or taking over the physical controller |
| Internet | No implementation of a PS5 connection |
| Security | No modification of Sony's program or access to the PSN account |

DualSense emulation, adaptive triggers, advanced game profiles, TDP modification, and other Handheld Companion features are outside the first version's scope.

## 3. Architecture

Proposed stack: C# / .NET 10 and [VIIPER](https://github.com/Alia5/VIIPER). VIIPER documentation confirms virtual DS4 support, including two touch contacts, a gyroscope, an accelerometer, and rumble feedback.

```text
ASUS ROG Xbox Ally X — built-in input devices
  XInput: buttons and sticks
  Sensor / HID: gyro and accelerometer
  Windows Touch: touchscreen
                  ↓
AllyRemoteBridge.exe
  Input Manager • Motion Manager • Touch Manager
  DS4 Mapper • Session Manager • Settings & Diagnostics
                  ↓
libVIIPER.dll + usbip-win2 driver
                  ↓
Virtual USB DualShock 4
                  ↓
Official PS Remote Play — unmodified Sony client
```

### Why libVIIPER?

The library can run directly inside our process, without a separate, continuously running application server. It also allows virtual devices to be created and removed at runtime.

Important technical limitations:

- The system USB/IP driver requires a one-time installation.
- Windows sees the emulated DS4 as a global device, not one assigned exclusively to Remote Play.
- USB/IP connections must be restricted to the local computer.
- `libVIIPER` is GPL-3.0 licensed, so this project must comply. This is acceptable for our open-source project.

Important security caveat: VIIPER documentation warns that some `usbip-win2` installers add a publicly known test certificate to Windows' trusted root certification authorities. Our installer must not blindly use such a package. We require a verified, properly signed driver package that does not leave such a certificate in the system.

## 4. Application modules

### 4.1. Input Manager — controller input

Reads the Xbox Ally X controller without taking exclusive control.

Implementation:

- Preferred API: XInput.
- Optional fallback: Windows GameInput if XInput behavior proves problematic.
- Automatically discover the built-in controller.
- Read buttons, sticks, and triggers.
- Handle disconnection, controller mode changes, and rediscovery.
- Configurable stick dead zones.

The physical controller remains visible to Windows. We do not install HidHide, replace ASUS drivers, or change Armoury Crate modes.

If several controllers are available, the application must identify the correct device or allow a one-time selection.

### 4.2. DS4 Mapper — input translation

Transforms Xbox controller input into a complete DualShock 4 state.

| ASUS / Xbox | DualShock 4 |
| --- | --- |
| A | Cross (×) |
| B | Circle (○) |
| X | Square (□) |
| Y | Triangle (△) |
| LB / RB | L1 / R1 |
| LT / RT | L2 / R2 |
| LS / RS | L3 / R3 |
| View | Share |
| Menu | Options |
| D-Pad | D-Pad |
| Left and right sticks | Corresponding DS4 sticks |
| Button combination | PS / touchpad click |

The Xbox button is handled by the system, so unrestricted interception is not assumed. Likewise, rear M1/M2 buttons are used only if ASUS exposes them as separate inputs.

The module must also:

- Forward simultaneous button presses.
- Support the full analog trigger range.
- Avoid accidental combinations when activating a shortcut.
- Immediately reset button state if the physical controller disconnects.
- Maintain an appropriate DS4 report rate without unnecessary updates.

### 4.3. Motion Manager — gyroscope and accelerometer

This is the most hardware-specific part of the application.

The ASUS ROG Xbox Ally X (2025, RC73XA) has a six-axis IMU, and ASUS support provides a Bosch G-sensor driver.

Implementation plan:

1. Detect the sensor at startup.
2. Attempt to read through Windows Sensor API.
3. If readings are unavailable or incorrect, support the Sensor HID/Bosch device through an appropriate hardware layer.
4. Calibrate the sensor orientation relative to the Ally X enclosure.
5. Convert units and axes to the format expected by the DS4.
6. Forward readings to libVIIPER.

VIIPER documents gyro values in degrees per second and acceleration in m/s², with conversion factors.

Functional requirements:

- Immediate response to device rotation.
- Correct orientation of all three axes.
- Neutral-position calibration when needed.
- Filter small amounts of noise without excessive latency.
- Preserve correct gravity in accelerometer readings.
- Stop sensor reading when the session ends.

We do not implement gyro-as-mouse. We forward real motion data to the virtual DualShock so PS5 games supporting motion controls can use it without additional mappings.

If sensor access fails, the rest of the application must continue working and clearly report that gyro is unavailable.

### 4.4. Touch Manager — DS4 touchpad emulation

Two independent modes are proposed.

#### Mode A: controller shortcuts

Provides a simple touchpad click, for example to open an in-game map.

Default combinations:

- `View + X` — touchpad click.
- Hold `View + Menu` — PS button.

Shortcuts are configurable. Their component buttons must not trigger extra in-game actions.

#### Mode B: touchpad on the Ally X screen

Activating a dedicated mode displays a translucent touch area over Sony's application.

Dragging a finger generates DS4 touch movement. Two fingers can generate two independent contacts.

The area can be moved and resized in settings. Closing the mode restores normal Windows touch behavior.

Technical implementation: a small Win32/WPF window handling `WM_POINTER` and touch contact IDs. Map screen coordinates to the DS4 touchpad space: X 0–1920, Y 0–942.

The overlay must not permanently capture touch across the entire screen, which would interfere with Sony and Windows controls.

Touch mode has an activation toggle and automatically deactivates when Remote Play loses focus. A touchpad click remains a separate action from touching its surface.

### 4.5. DS4 Emulator — virtual device

Uses `libVIIPER.dll` and manages the virtual controller lifecycle.

Responsibilities:

- Check that the required driver is available.
- Create a virtual USB DS4.
- Update its state from other modules.
- Receive rumble feedback.
- Remove the virtual device at session end.
- Handle USB/IP attachment and detachment errors.

The emulator runs as a single instance. Repeated application launches must not create additional DS4 devices.

### 4.6. Rumble Manager — vibration

VIIPER can forward DS4 motor intensity feedback.

The module should attempt to map it to ASUS controller vibration, preferably through XInput.

This is secondary functionality. If rumble conflicts with ASUS mechanisms or the current Remote Play client does not forward it, it remains disabled by default. Full DualSense haptic fidelity is not required.

## 5. Session Manager — the key isolation component

The application should follow this state machine:

| State | Behavior |
| --- | --- |
| IDLE | No controller is emulated |
| PREPARING | Detect ASUS controller, sensors, and driver |
| ATTACHING | Create DS4 and confirm Windows detection |
| LAUNCHING | Launch the official Sony application |
| ACTIVE | Forward input during the session |
| STOPPING | Reset input, detach DS4, and exit the bridge |

### Operating rules

#### Startup

Launch through a single desktop or Armoury Crate shortcut. Check Sony processes, prepare the virtual DS4, and only then launch Remote Play, giving Sony an opportunity to detect the controller from the start.

#### Monitoring

Identify Sony's client using its verified executable path and process, not just its window title. Do not inject code or modify process memory.

#### Switching applications

When Sony is running but the user switches to another application, the bridge should send neutral button state. Forward controller input only during an active session.

The virtual DS4 remains visible to Windows. Stopping reports alone does not guarantee that other games will not detect another controller. This limitation is accepted for version 1.0.

#### Shutdown and failures

When Sony closes, detach the controller, release USB/IP resources, and exit. Also handle:

- Remote Play or bridge crashes.
- Ally X sleep and resume.
- Physical controller loss.
- Sony restarting during an update.
- Windows leaving an orphaned virtual device.

Provide `--cleanup` to safely remove bridge-owned devices when normal shutdown fails.

## 6. Interface and configuration

The application should not require an extensive GUI. Three ways to use it are proposed.

### Default: one shortcut

Clicking `Ally Remote Bridge` launches everything. No additional windows, profiles, or switches.

### Settings: a small window

Available on demand. Configure shortcuts, touchpad, gyro, rumble, input device, and application behavior.

### Diagnostics: Windows tray icon

During a session, inspect device status, open diagnostics, or disable emulation in an emergency. The icon disappears when the session ends.

Example minimal settings interface (design only, not an existing application):

```text
Ally Remote Bridge
  Controller
    Target device: DualShock 4
    Stick dead zone: 8%
    Enable gyro and accelerometer
    Enable on-screen touchpad
    Forward rumble
  Startup
    Automatically launch PS Remote Play
  After Remote Play exits
    Close bridge
```

Store configuration in a local JSON file under `%LOCALAPPDATA%\AllyRemoteBridge\`. No settings require a user account or cloud synchronization.

## 7. Installation and distribution

The final package should contain a small installer and the application files.

### One-time installation

- Verify Windows 11, x64 architecture, and compatible drivers.
- Install the required USB/IP driver after user confirmation.
- Verify signatures, versions, and certificates.
- Create an application shortcut.
- Test the installation and allow components to be uninstalled.

### Everyday use

- No administrator requirement, provided driver configuration permits normal user operation.
- No service continuously running at Windows startup.
- No additional physical controller driver.
- No automatic update installation.
- No interference with Armoury Crate.

Uninstallation removes bridge files and settings. Remove the USB/IP driver only on explicit request and only when no other software uses it.

## 8. Security and privacy

Because security matters more than maximizing the feature count, adopt these requirements:

| Area | Rule |
| --- | --- |
| PlayStation Network | No access to credentials or tokens |
| Official Remote Play | No modification, patching, or code injection |
| Network | Bridge does not connect to PS5 or Sony services |
| USB/IP | Local connections only; no public interface listeners |
| Drivers | Verified, signed packages; no Windows test mode |
| Privileges | Administrator only for required installation operations |
| Telemetry | None |
| Logs | Local only; no account data or screen capture |
| Updates | Manual, with integrity checks |
| Source code | Public repository, GPL-3.0 compatible |

Using Sony's official application does not guarantee acceptance of a virtual controller. We will not bypass client integrity checks or force installation of older versions.

If safe USB/IP installation cannot be ensured, treat it as a release blocker rather than something users must work around.

## 9. Project structure

```text
AllyRemoteBridge/
├── src/
│   ├── AllyRemoteBridge.App/
│   │   ├── Program.cs
│   │   ├── Tray/
│   │   └── Settings/
│   ├── AllyRemoteBridge.Core/
│   │   ├── SessionManager.cs
│   │   ├── InputManager.cs
│   │   ├── MotionManager.cs
│   │   ├── TouchManager.cs
│   │   ├── DS4Mapper.cs
│   │   └── RumbleManager.cs
│   ├── AllyRemoteBridge.Devices/
│   │   ├── XInputController.cs
│   │   ├── SensorReader.cs
│   │   └── ViiperDS4.cs
│   └── AllyRemoteBridge.Overlay/
│       └── TouchpadWindow.cs
├── tests/
│   ├── UnitTests/
│   ├── IntegrationTests/
│   └── HardwareTests/
├── installer/
├── docs/
├── LICENSE
└── README.md
```

This separation allows mapping logic, hardware support, and VIIPER integration to be tested independently. Changing gyro acquisition should not require rewriting the application.

## 10. Implementation schedule

Do not divide the project into separate demonstrations. Build the intended architecture from the beginning, deliver it in stages, and test each significant change.

### Phase 1 — Foundation and hardware integration

Create the repository, configure .NET, add device diagnostics, XInput reading, a VIIPER wrapper, driver detection, and privilege checks.

Completion criterion: create one DS4, read the physical controller, and correctly remove the virtual device on exit.

### Phase 2 — Full mapping and Sony compatibility

Forward all buttons and sticks, implement basic shortcuts and controller state management, and integrate with current PS Remote Play.

Completion criterion: Sony's official client accepts the virtual DS4 and allows console control.

This is the most important decision gate. If Sony rejects the controller, do not develop the remaining features without resolving this through a compliant and safe method.

### Phase 3 — Gyro and accelerometer

IMU detection, Bosch sensor support, calibration, axis and unit conversion, noise filtering, and VIIPER integration.

Completion criterion: rotating the handheld produces corresponding behavior in a game with motion controls.

### Phase 4 — Touchpad and screen overlay

Touch input, two fingers, touchpad click, shortcut activation, and disabling the overlay when switching applications.

Completion criterion: games correctly recognize touchpad clicks and gestures, and the overlay does not interfere with normal Windows operation.

### Phase 5 — Automation and fault tolerance

Session Manager, Remote Play detection, application switching, sleep, resume, shutdown, crashes, and controller reconnection.

Completion criterion: normal shutdown and common failures do not leave an additional active controller.

### Phase 6 — Complete application release

Settings window, tray icon, installer, local diagnostics, documentation, regression tests, and safe uninstallation procedure.

Completion criterion: install and use the application without manually configuring libraries or running terminal commands.

## 11. Acceptance tests

The project is not complete merely because Windows detects a virtual DS4.

| Scenario | Required result |
| --- | --- |
| Launch a game through PS Remote Play | All buttons work |
| Session away from home | Input also works over the internet |
| Game using the touchpad | Clicks and swipes are recognized |
| Game with motion controls | Gyro follows Ally X movement |
| Return to an Xbox / Steam game | Physical controller works unchanged |
| Close Remote Play | Virtual DS4 is removed |
| Sleep and resume | No persistent loss of control |
| Bridge crash | Safe recovery to a correct state is possible |
| Uninstall | No unwanted settings remain |

Additional performance targets requiring hardware measurements:

- Bridge input latency overhead: target below 5 ms.
- CPU use during a normal session: target below 2% of the whole system.
- No noticeable reduction in Remote Play smoothness.
- No active sensor polling after session end.

These are design targets, not verified results.

## 12. Risks and architectural decisions

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Sony rejects the emulated DS4 | Critical | Early compatibility testing |
| Unsafe USB/IP driver package | Critical | Installer and certificate audit |
| ASUS sensor access problems | Medium | Separate hardware adapter |
| Touch overlay conflicts with Sony UI | Medium | Activate only on demand |
| Other applications detect virtual DS4 | Medium | Short device lifecycle |
| Sony or Windows updates change behavior | Medium | Regression tests and pinned dependencies |

Use existing Handheld Companion code as a source of knowledge about controller and sensor support rather than copying the entire project. This keeps application scope small and dependencies controlled.

## 13. Definition of version 1.0 completion

The project is complete when:

- Current official PS Remote Play works with the virtual DS4.
- All standard Xbox Ally X controls are supported.
- Touchpad clicks, gestures, and two contacts work.
- Gyro and accelerometer forward real motion data.
- The application starts and stops alongside Remote Play.
- It does not install HidHide, change Armoury Crate, or take over the physical controller.
- Installation is safe, reversible, and does not require disabling Windows security.
- Use away from home requires no bridge configuration or changes to Sony's PS5 connection method.

Final architectural decision: one small .NET application with `libVIIPER`, built-in session management, a lightweight touch overlay, and a separate ASUS IMU adapter. No additional continuously running service, HidHide, DualSense emulation, or advanced Handheld Companion features.

The only persistent system component currently unavoidable in this architecture is the USB/IP driver.

This plan defines the complete product scope but does not establish Sony compatibility. That must be confirmed on a real Ally X with current Remote Play. It is a version 1.0 release condition, not an assumption that can be treated as satisfied without testing.
