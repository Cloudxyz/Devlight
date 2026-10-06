# Validation — automatic restoration, 2026-10-06

## Feature delta

Final combined validation: **69 checks passed**, including real hardware and the published executable.

- Debug and Release builds: zero warnings/errors.
- Self-contained single-file `win-x64` publication: passed. The initially running previous executable was closed to release the output file before publication.
- Shared restoration worker: temporary failures recover, success stops retrying, exhausted failures stop silently, event bursts coalesce, manual requests supersede automatic work, updated settings replace old targets, and stopping cancels delayed work.
- Native power/monitor registrations succeeded. Simulated resume, display, monitor interface arrival, topology and session display-on messages reached detection; unrelated device arrivals and display-off messages did not request restoration. Disposal removed the hidden native window.
- Original manual apply, first-run Settings, Settings persistence, HKCU startup toggling, tray-only restart, duplicate-process rejection and clean exit remain covered.

Physical FY27QHC-B validation used an original brightness of **14**. Startup restored the saved **30** without a click. After resetting the hardware, the manual left-click handler restored **30**. Simulated display-change, resume and monitor-arrival notifications each restored **30** through the same native operation. Saving **32** in Settings applied it immediately, and the next simulated resume used **32**. The actual published executable, launched after brightness was changed while Devlight was closed, automatically restored saved **30** with no visible window. Original hardware **14**, configuration and startup registration were restored afterward.

Saving an unavailable identity started a bounded silent sequence; application Exit canceled that pending sequence and removed the tray icon. Delayed hardware readiness was modeled with controlled operation failures followed by success; the monitor was not physically made unresponsive. A later hardware power-on with no Windows event is detectable only within the active retry window, otherwise a new event or manual click is needed.

Physical sleep, hibernation, login/reboot, monitor power-button wake and cable disconnect/reconnect were not induced. Those scenarios still require manual acceptance testing. Resume/display/reconnect **message handling with real brightness writes** was tested; this is not a claim of a physical suspend or reconnect cycle.

Affected files: `BrightnessRestorer.cs`, `DisplayEventWindow.cs`, `TrayApplicationContext.cs`, `MonitorService.cs`, `NativeMethods.cs`, `SettingsForm.cs`, `Validation/Program.cs`, `Validation/RestorationChecks.cs`, `Validation/AutomaticHardwareChecks.cs`, `README.md`, `VALIDATION.md`.

## Initial release validation — 2026-10-06

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
