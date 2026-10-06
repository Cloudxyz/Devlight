# Validation — 2026-10-06

Validated on the available Windows x64 desktop with .NET SDK 10.0.401.

## Build and distribution

- Restore: passed.
- Debug build: passed, zero warnings/errors.
- Final Release build: passed, zero warnings/errors.
- Self-contained, single-file `win-x64` publish: passed.
- Portable executable: `artifacts/publish/win-x64/Devlight.exe` (approximately 111 MiB).

## Application checks

The dependency-free validation harness passed settings recovery (missing, empty, malformed, null, outdated and out-of-range), atomic save/replacement, identity matching, percentage scaling/clamping, native discovery and disconnected-monitor error handling.

Desktop checks passed first-run Settings, tray registration, Cancel returning to tray only, Settings saving/changing monitor and brightness, configuration surviving a new application context, HKCU startup enable/disable with a quoted executable path, and an unavailable saved monitor leaving the tray process running. Configuration and the original startup registration were restored after testing.

The actual published executable passed automatic first-run Settings, native taskbar exclusion, a duplicate process exiting with code 0 while the first remained alive, closing Settings leaving the application running, clean message-loop termination, and restart with a saved configuration producing no visible window. The smoke harness terminates the portable process through its message loop; the application's Exit cleanup is exercised separately through `ExitAsync` in the desktop/hardware checks.

Final hardware/portable run: **29 checks passed**. Desktop run: **23 checks passed**. No third-party test dependencies are used.

## Physical hardware

Windows discovered three compatible displays:

- FY27QHC-B
- HP 22er
- S24D332

The FY27QHC-B responded to a real brightness write through the tray left-click event handler: **30 → 31**, with native readback confirming 31. Its original **30** was restored and confirmed by readback. Hardware firmware needed a brief delay before reads reflected writes; the validation harness allows that delay. This is an automated invocation of the tray's actual left-click event handler, rather than a physical mouse gesture.

The first physical-monitor token returned by Windows was **0**, and worked for reads, writes and cleanup. Successful native acquisition is therefore tracked explicitly; zero is not treated as an invalid token.

Brightness writes were not exercised on the HP/Samsung screens. Physical unplugging, sleep, DDC/CI being disabled in the monitor OSD, and ambiguous mirror/tiled groups were not induced. A nonexistent saved identity was exercised and handled without crashing. Manual mouse interaction with the shell's tray icon remains a user acceptance check.

## Changed files

- `Devlight.csproj`, `app.manifest`, `.gitignore`
- `Program.cs`, `TrayApplicationContext.cs`, `SettingsForm.cs`
- `NativeMethods.cs`, `MonitorService.cs`
- `AppSettings.cs`, `SettingsStore.cs`, `StartupRegistration.cs`
- `Assets/Devlight.ico`, `Properties/PublishProfiles/Portable.pubxml`
- `Validation/Devlight.Validation.csproj`, `Validation/Program.cs`
- `README.md`, `VALIDATION.md`

## Known limits

Monitor interface identity survives enumeration reordering but Windows may change it after port/dock/driver changes. Reconfiguration is then required. Ambiguous physical/interface mappings are rejected. Some hardware cannot expose DDC/CI through its connection; native calls can take time on unresponsive monitors. Tray notifications depend on Windows notification settings.
