# Devlight

Devlight is a small Windows tray application. Select one physical monitor and a target brightness once, then left-click the sun icon to apply it. It never changes brightness simply because Windows or Devlight starts.

## Requirements and use

Windows 10/11 x64 and an external monitor that exposes brightness through Windows' DDC/CI monitor configuration APIs. Enable **DDC/CI** in the monitor's own OSD if necessary. Laptop panels, some docks/adapters, sleeping monitors and remote desktop displays may not expose this control. Monitorian is not required.

Launch `Devlight.exe`. On first run, Settings opens automatically. Select a compatible monitor, choose 0–100%, and Save. Unsupported displays show their reason and cannot be saved. Settings has no taskbar entry; closing it leaves Devlight in the tray. Windows may put the sun icon in its hidden-icons overflow.

- **Left click / Apply brightness:** apply the saved target immediately.
- **Settings:** change the monitor or percentage. Saving does not apply brightness.
- **Start with Windows:** toggle the current user's startup registration, without administrator rights.
- **Exit:** release resources and remove the tray icon. A second instance exits immediately.

Settings are stored at `%LOCALAPPDATA%\Devlight\settings.json` with an atomic replacement. Missing or invalid settings reopen Settings. Expected hardware failures show a concise tray notification; diagnostic exceptions/native errors are available in debugger output. If Windows notifications are disabled, those tray notifications may be hidden.

Startup uses only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Devlight`. Keep the executable in a stable folder. Enabling the option or saving Settings again refreshes its path after a move. Exit does not disable startup.

Monitor identity uses the Windows monitor device interface path from `EnumDisplayDevicesW` with `EDD_GET_DEVICE_INTERFACE_NAME`, associated with the physical-monitor handle. Matching is isolated in `MonitorIdentity`. It does not rely on enumeration indices or names. Windows can change this path after a driver/port/dock change; reselect the monitor if that happens. Ambiguous mirror/tiled groups are rejected rather than assigned an arbitrary physical handle; use extended displays. Brightness reads determine actual compatibility, even when reported capability flags are inaccurate. There is no polling, and handles exist only during discovery or an apply operation.

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

Native API references: [monitor configuration](https://learn.microsoft.com/en-us/windows/win32/api/_monitor/), [monitor interface identity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw).
