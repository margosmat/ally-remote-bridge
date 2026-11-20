# Implementation and release status

The repository began empty. `plan.md` is an English translation of the supplied specification; embedded search-result badges and illustrative remote-image clutter were replaced with readable Markdown and text diagrams. This document records implementation status separately from that specification.

## Phase coverage

| Phase | Implemented source | Outstanding acceptance / work |
| --- | --- | --- |
| 1: foundation | .NET solution, nonexclusive XInput, native ABI, loopback VIIPER lifecycle, USB/IP attachment, single instance, ownership journal, HID enumeration | Run attach/report/removal and crash recovery with a reviewed signed driver on Windows |
| 2: mapping and Sony | Buttons, sticks, analog triggers, dead zones, deferred-prefix shortcuts, foreground neutralization, Sony signature/path checks | Confirm current official Sony client accepts the virtual DS4 and controls PS5; this gate remains unresolved |
| 3: motion | Windows gyro/accelerometer acquisition, physical units, signed axis mapping, bias calibration, filtering, stale-data rejection, suspend and teardown | Validate RC73XA access and all axes/gravity; if Windows does not expose the IMU, implement a raw Bosch adapter using captured and verified hardware reports |
| 4: touchpad | Two stable contact slots, touch surface coordinate conversion, WM_POINTER overlay, separate click, configurable geometry and toggle, focus-loss hiding | Test actual touchscreen contact lifecycle and gestures in PS5 games; Windows UI visual QA |
| 5: automation | Session state machine, launch ordering, process-path tracking, restart grace, disconnect neutralization, suspend/resume, cleanup on failures, scoped recovery | Exercise Sony updater/relaunch, process termination, driver cancellation/timeouts, USB/IP reenumeration, Windows logout and recovery on hardware |
| 6: distribution | Settings window, tray controls, diagnostics, bounded local logs, dependency verification, root-device driver installation/rollback source, Inno installer source, CI, GPL license and documentation | Approve signed native dependencies, compile and test installer on Windows, verify trust stores unchanged, uninstall QA, public source distribution, release signing |

**All six phases have implementation work present. All six acceptance gates are not complete. Version 1.0 is not finished or safe to label as verified.** No Windows, Ally X, PS5, driver-installation, Sony, or Windows UI test has run in this macOS environment.

The plan's phase-2 decision gate remains mandatory for release. Later-phase source exists for development, but it is not evidence that Sony compatibility is solved.

## Verified locally

- .NET 10 solution compilation, including Windows UI and sensor references, with warnings treated as errors.
- 30 portable regression cases for mapping, shortcut suppression and timing, touch slots and bounds, motion units/bias/axes/gravity/staleness/saturation, settings validation/persistence, C# native structure padding, USB/IP ownership parsing, session ordering, cancellation, focus loss, disconnect, process exit, attach/report errors, suspend/resume, and duplicate sessions.
- Independent C11 structure-size and offset assertions compiled with Clang and warnings treated as errors.

Exact commands and latest results are recorded in `docs/validation.md`.

## Explicit limitations

- A raw Bosch HID protocol adapter is not implemented. Guessing report formats or claiming hardware support from a portable test would be unreliable. Windows sensor acquisition degrades gracefully when unavailable.
- XInput identifies controller slots, not stable ASUS device identities. Selection is explicit when ambiguous; automatic GameInput identity support is not implemented.
- Sony publisher names are allowlisted after Windows trust verification. A legitimate publisher-name change requires review and a software update, not bypassing signature checks.
- The USB/IP control ABI is pinned to the inspected upstream header revision. An incompatible driver is rejected rather than falling back to shell commands or detaching unrelated ports.
- Crash cleanup depends on the driver remaining responsive and the persisted ownership record. `--cleanup` fails visibly and retains the record if it cannot confirm removal; recovery under all driver failures has not been demonstrated.
- The installer source has not been compiled or executed here. Driver and installer release review is outstanding; example approval data cannot produce a release.
- Performance targets are unmeasured.
