# Local validation

Date: 2026-11-18. Host: macOS arm64. .NET SDK 10.0.300; .NET runtime 10.0.8.

| Check | Result |
| --- | --- |
| `dotnet build AllyRemoteBridge.sln -c Release --no-restore --disable-build-servers -m:1` | PASS: zero warnings and errors |
| `dotnet tests/AllyRemoteBridge.Tests/bin/Release/net10.0/AllyRemoteBridge.Tests.dll` | PASS: 30/30 regression cases |
| `clang -std=c11 -Wall -Wextra -Werror tests/NativeAbi/layout.c -o /tmp/ally-native-layout` and execution | PASS: DS4 and USB/IP native layout assertions |
| `dotnet publish src/AllyRemoteBridge.App -c Release -r win-x64 --self-contained true -o artifacts/development --disable-build-servers` | PASS: Windows x64 self-contained development output |
| Windows runtime, driver, sensors, Sony, touch UI, installer and uninstall | NOT RUN: Windows/Ally X/PS5 unavailable |
| Latency and CPU targets | NOT MEASURED |

The published development output is ignored by Git. It contains the Windows application and desktop runtime, but no reviewed VIIPER DLL or USB/IP driver. It is not a complete release package. `--settings` can run without those dependencies; emulation deliberately fails until the reviewed payload is present.

The portable tests use fake hardware/process implementations for session behavior. The native layout checks compare ABI structure sizes/offsets; neither test suite proves that the real driver accepts the requests or Sony accepts the emulated controller.

CI source includes Linux and Windows builds, portable regressions, Linux native layout checks, Windows PowerShell syntax parsing, and a Windows development publish. CI has not been executed from this workspace, and no remote repository or release was created.
