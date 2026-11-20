# Dependency provenance and packaging

Do not ship an arbitrary latest native DLL or driver. Managed declarations and byte layouts were inspected against these revisions:

- [VIIPER a77bbbb78a6072188fe40526acbcba5d9d70d197](https://github.com/Alia5/VIIPER/tree/a77bbbb78a6072188fe40526acbcba5d9d70d197), particularly `lib/viiper/server.go`, `lib/viiper/dualshock4.go`, and `device/dualshock4/const.go`.
- [usbip-win2 77a781eaedc7f6b6cb171a92d378dee408ad7408](https://github.com/vadimgrn/usbip-win2/tree/77a781eaedc7f6b6cb171a92d378dee408ad7408), particularly `include/usbip/vhci.h`, `include/usbip/consts.h`, `drivers/ude/vhci_ioctl.cpp`, and `drivers/ude/usbip2_ude.inf`.

VIIPER's high-level DS4 documentation omits the current creation function's final metadata pointer. Use the pinned generated header when producing a native build. The C input state is 34 bytes with alignment; the 31-byte TCP protocol is a different layout. The libVIIPER D-pad input uses direction bits despite conflicting hat constants in the exported C comments. Tests cover these distinctions.

USB/IP is bound to `127.0.0.1:32451`. The bridge creates its own bus, exports one DS4, then uses the native `PLUGIN_HARDWARE_ONCE` request. It never invokes `usbip.exe`, opens a public listener, configures persistence, or issues detach-all. Recovery matches the recorded bus, loopback host, service, serial, and (when known) port. The journal is written before attachment and retained if cleanup fails.

## Reviewed payload

A complete application directory contains:

```text
AllyRemoteBridge.exe and managed/runtime files
native/
  libVIIPER.dll
  manifest.json
  source-and-licenses/
driver/
  usbip2_ude.inf
  usbip2_ude.cat
  usbip2_ude.sys
  approved-driver.json
  source-and-licenses/
```

`native/manifest.json` contains `sourceRevision` and the actual reviewed DLL `sha256`. The runtime hashes the DLL before loading it from its absolute path with restricted DLL dependency search. The manifest provides integrity against accidental mismatch; installer/package authenticity and trusted distribution remain necessary.

Copy `installer/approval.example.json` outside the repository, complete an actual security review, and replace the placeholders with hashes and the catalog signer's certificate thumbprint. Do not merely flip the boolean. Verify the driver artifacts correspond to the declared source revision, include the required ABI, and have a production signature acceptable to Windows kernel policy. If no such package is available, distribution is blocked.

The packaging script verifies catalog membership with Windows SDK `signtool /kp`, copies only the INF, catalog, and driver, and requires corresponding-source/license directories. It never runs the upstream driver installer. Driver installation checks Windows code integrity and rejects test mode, verifies payload hashes and the pinned trusted catalog signer, registers the known root device if absent, and asks Windows to install the INF. Failed installation attempts remove only a newly registered root device.

The installer offers driver installation as an explicit optional task. Windows may require a reboot. Everyday bridge launches use `asInvoker`. The shared USB/IP driver is preserved on uninstall; deliberate driver removal requires confirming no other application uses it. No driver-removal shortcut or automatic uninstall of shared components is provided.

Uninstallation uses scoped cleanup and deletes the current uninstalling user's bridge settings. For installations/uninstallations run under a different administrator account, clean each original user's local bridge settings explicitly. Installer verification must cover this scenario before release.

## Licensing

This application is GPL-3.0-only to support linking with libVIIPER. Preserve upstream notices and provide complete corresponding source and build instructions for distributed components. No Handheld Companion implementation has been copied. Dependency directories must contain their actual upstream licenses and source, not empty placeholders.
