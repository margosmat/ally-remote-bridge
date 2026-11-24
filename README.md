# Ally Remote Bridge

A Windows 11 x64 background bridge from the ASUS ROG Xbox Ally X controller, touchscreen, and IMU to a virtual USB DualShock 4 for official PS Remote Play.

**Development implementation, not a verified 1.0 release.** The solution builds and portable regressions pass. Sony compatibility, the Ally X sensor orientation, a reviewed signed USB/IP package, and Windows installation/recovery tests remain release gates. No native driver or VIIPER binary is bundled in this repository.

The English product specification is in [plan.md](plan.md). Current implementation and verification status is in [docs/implementation-status.md](docs/implementation-status.md).

## Build and verify

Install the .NET 10 SDK. The repository uses nuget.org for Windows targeting packs.

```sh
dotnet build AllyRemoteBridge.sln -c Release
dotnet run --project tests/AllyRemoteBridge.Tests -c Release --no-build
```

Portable tests run on macOS, Linux, and Windows. Windows project compilation is supported from other operating systems; their UI, sensor APIs, and driver operations run only on Windows. The test runner is an executable with a nonzero failure exit code; `dotnet test` is not its entry point.

Optional independent native layout checks:

```sh
cc -std=c11 -Wall -Wextra -Werror tests/NativeAbi/layout.c -o /tmp/ally-native-layout
/tmp/ally-native-layout
```

Produce a Windows development application:

```sh
dotnet publish src/AllyRemoteBridge.App -c Release -r win-x64 --self-contained true -o artifacts/development
```

This output needs the reviewed native dependency payload described in [docs/dependencies.md](docs/dependencies.md) before it can emulate a controller. Settings can be opened without a driver or VIIPER DLL. A self-contained distribution includes the .NET desktop runtime, so it is larger than the application source alone.

## Use on Windows

After installation of a reviewed release, launch **Ally Remote Bridge**. The application prepares a DS4, waits for Windows HID enumeration, launches the verified Sony executable, and forwards input while Sony has foreground focus. The tray icon provides settings, diagnostics, a touchpad toggle, and emergency stop.

Default shortcuts (press the prefix first):

- `View + X`: hold a touchpad click.
- Hold `View + Menu` for 600 ms: PS button.
- `View + Y`: toggle the touch overlay.
- Tap `View` on its own for Share, or hold it to hold Share.

Press the shortcut action within 250 ms of pressing View. After that interval an unclaimed View press is forwarded as held Share until release. Pressing an action button before the prefix can already send that button to the game. Shortcut buttons are configurable. Touching the surface does not click it.

The overlay captures only its own rectangle, supports two contacts, and disappears when Sony loses foreground focus. Set its size and position in settings.

```text
AllyRemoteBridge.exe                 Start a session
AllyRemoteBridge.exe --settings      Configure the next session
AllyRemoteBridge.exe --diagnostics   Inspect controller slots and sensor availability
AllyRemoteBridge.exe --verify-device Check real Windows DS4 attach/report/removal
AllyRemoteBridge.exe --cleanup       Recover only a recorded bridge-owned device
```

The internal `--install-driver` switch is for the elevated installer; everyday use runs without elevation. `--installer-cleanup` is a noninteractive uninstall operation.

Configuration and bounded local logs live in `%LOCALAPPDATA%\AllyRemoteBridge\`. There are no accounts, telemetry, cloud configuration, or Sony credential handling. Certificate trust validation may use Windows' normal revocation checks.

When more than one XInput controller is connected, choose the Ally's slot in settings. The application does not silently switch slots after a disconnect. XInput slots are not permanent device identities: check the selection after connecting other controllers or changing ASUS controller modes.

The physical Xbox controller stays visible. The virtual DS4 is also globally visible for the lifetime of the session; other games can discover it. Input is neutralized on focus loss, and the DS4 is removed after Sony exits (default restart grace: 10 seconds).

## Release and hardware validation

[installer/Prepare-Release.ps1](installer/Prepare-Release.ps1) verifies explicitly approved dependency hashes, the catalog signer, and kernel-policy catalog membership before creating the Inno Setup payload. The example approval file is deliberately unapproved. Public distribution also requires dependency source and licenses.

Follow [docs/hardware-validation.md](docs/hardware-validation.md) on a real RC73XA and PS5. Record results in [tests/HardwareTests/acceptance.json](tests/HardwareTests/acceptance.json). A passing portable test suite must never be substituted for those checks.

## License

GPL-3.0-only. See [LICENSE](LICENSE) and [docs/dependencies.md](docs/dependencies.md).
