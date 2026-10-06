# Devlight

Devlight is a small Windows tray application. Select one physical monitor and a target brightness once. Devlight restores that brightness automatically at launch, after resume, and when Windows reports display or monitor availability changes. Left-click the sun icon to apply it manually at any time.

## Requirements and use

Windows 10/11 x64 and an external monitor that exposes brightness through Windows' DDC/CI monitor configuration APIs. Enable **DDC/CI** in the monitor's own OSD if necessary. Laptop panels, some docks/adapters, sleeping monitors and remote desktop displays may not expose this control. Monitorian is not required.

Launch `Devlight.exe`. On first run, Settings opens automatically. Select a compatible monitor, choose 0–100%, and Save. Unsupported displays show their reason and cannot be saved. Settings has no taskbar entry; closing it leaves Devlight in the tray. Windows may put the sun icon in its hidden-icons overflow.

- **Left click / Apply brightness:** apply the saved target immediately.
- **Settings:** change the monitor or percentage. Saving a new target applies it immediately and uses it for future restoration.
- **Start with Windows:** toggle the current user's startup registration, without administrator rights.
- **Exit:** release resources and remove the tray icon. A second instance exits immediately.

Settings are stored at `%LOCALAPPDATA%\Devlight\settings.json` with an atomic replacement. Missing or invalid settings reopen Settings. Manual hardware failures show a concise tray notification; automatic failures stay silent and never open Settings. Diagnostic exceptions/native errors are available in debugger output. If Windows notifications are disabled, those tray notifications may be hidden.

Automatic restoration makes one immediate attempt and, only on failure, retries after waits of 2, 3, 5, 10, 20 and 20 seconds (roughly one minute, plus native request time). It stops immediately after success or after seven attempts. Startup through **Start with Windows** uses the same sequence, allowing the monitor time to become ready after login.

A hidden native window receives system resume, display configuration, monitor interface arrival/removal, device topology and session display-on notifications. Bursts for the same target join one sequence. Manual clicks and changed Settings replace pending work; native operations never overlap. Exit cancels delayed retries and waits for in-progress native handles to be released. There is no continuous availability polling while healthy or after retry exhaustion.

Some monitors' physical power buttons produce no Windows event while the PC remains awake. If such a monitor wakes within the retry window, a later attempt restores brightness. After that window, another Windows display/power event or a manual tray click is required; Devlight cannot detect an unreported physical wake without ongoing polling.

Startup uses only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Devlight`. Keep the executable in a stable folder. Enabling the option or saving Settings again refreshes its path after a move. Exit does not disable startup.

Monitor identity uses the Windows monitor device interface path from `EnumDisplayDevicesW` with `EDD_GET_DEVICE_INTERFACE_NAME`, associated with the physical-monitor handle. Matching is isolated in `MonitorIdentity`. It does not rely on enumeration indices or names. Windows can change this path after a driver/port/dock change; reselect the monitor if that happens. Ambiguous mirror/tiled groups are rejected rather than assigned an arbitrary physical handle; use extended displays. Brightness reads determine actual compatibility, even when reported capability flags are inaccurate. Handles exist only during discovery or an apply operation.

## Build and portable release

Install the .NET 10 SDK on Windows, then run from this directory:

```powershell
dotnet restore Devlight.csproj
dotnet build Devlight.csproj -c Debug
dotnet build Devlight.csproj -c Release
dotnet publish Devlight.csproj -p:PublishProfile=Portable
```

The portable release is `artifacts\publish\win-x64\Devlight.exe`. Distribute that executable alone; it includes the runtime and does not require a .NET installation. WinForms native runtime libraries extract under the user's temporary directory on launch. Trimming is disabled. No installer or third-party packages are used.

## Validation

```powershell
dotnet run --project Validation\Devlight.Validation.csproj -c Release
```

The dependency-free validation executable checks settings recovery and round trips, percentage translation, identity matching and real Windows monitor discovery. With `--desktop`, it also runs UI lifecycle and per-user startup checks, temporarily backing up/restoring Devlight's configuration and startup entry. Run validation only while Devlight is closed. `--hardware` additionally changes the first compatible monitor's brightness through the tray left-click handler, reads it back, then restores its original brightness. `--process artifacts\publish\win-x64\Devlight.exe` checks the published executable's first run, taskbar exclusion, single instance and tray-only restart. See `VALIDATION.md` for this machine's actual results.

Restoration tests run by default and cover delayed readiness, retry exhaustion, event coalescing, manual/settings replacement and shutdown cancellation. `--auto-hardware` adds physical readback checks for startup, simulated resume/display/reconnect messages and Settings Save. Combine it with `--process artifacts\publish\win-x64\Devlight.exe` to check actual portable startup restoration. Configuration, startup registration and tested hardware brightness are restored after validation. Simulated events do not replace manual sleep/hibernate/reconnect testing.

Native API references: [monitor configuration](https://learn.microsoft.com/en-us/windows/win32/api/_monitor/), [monitor interface identity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw).
