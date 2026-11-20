# HP Button Remap

Lightweight tray application for remapping HP laptop special function keys (MyHP/HP System Event Utility/HP Programmable Key). Tested on an HP OmniBook, YMMV on other models. This is a vibe coded app, use at your own risk.

## Features

- open an app or file (toggles the app's window if it is already running)
- open a website
- send a keyboard shortcut
- run a command (config file only)

## Requirements

- Windows 10/11
- HP device exposing `root\wmi` `hpqBEvnt` events
- .NET 10 Desktop Runtime (the MSI includes it)

## Install

1. Download `HPButtonRemap.exe`
2. Run it once

It self-installs to `%LOCALAPPDATA%\HPButtonRemap\`.

## Uninstall

Tray icon -> **Uninstall...**

## Configuration

Edit `%LOCALAPPDATA%\HPButtonRemap\config.json` (MSI installs: `%LOCALAPPDATA%\HPButtonRemapGUI\config.json`) and click tray menu -> **Reload Configuration**. Environment variables such as `%userprofile%` work in `Command` and `WorkingDirectory`. See [CONFIG-EXAMPLES.md](CONFIG-EXAMPLES.md) for every action type.

### Config shape

- `EventID` and `EventData` are root-level
- one `Action` object (not an array)

Built-in default `config.json`:

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "RunCommand",
    "Command": "%localappdata%\\Microsoft\\WindowsApps\\wt.exe",
    "CreateNewWindow": true
  }
}
```

## Full example configs

### 1) RunCommand

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "RunCommand",
    "Command": "%localappdata%\\Microsoft\\WindowsApps\\wt.exe",
    "CreateNewWindow": true,
    "WorkingDirectory": "%userprofile%",
    "DelayMs": 0
  }
}
```

### 2) OpenWebsite

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "OpenWebsite",
    "OpenWebsite": "https://learn.microsoft.com/windows/terminal/",
    "DelayMs": 0
  }
}
```

## Build locally

```powershell
dotnet publish .\HPButtonRemap\HPButtonRemap.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  --output .\publish\app
```

## Change what the key opens

Double-click the tray icon or choose **Settings…** from its menu. Choose **Open an app or file** and browse for an executable or document, or choose **Open a website** and enter an HTTP or HTTPS address. Click **Save** to apply the new action immediately. Existing command configurations still work and can be edited through **Open Configuration**.

For a portable run, use `HPButtonRemap.exe --portable --settings`. Set `RunAtStartup` to `false` in the adjacent config if you want to start it manually.

## Windows installer

Run the Windows x64 MSI and follow the setup wizard: accept the license, install, and optionally open Settings on the last page. Installation is per-user, includes the required .NET runtime, and leaves the hotkey unassigned and login startup disabled on first launch. Installing an update closes the running tray app automatically.

To uninstall, open **Uninstall HP Button Remap GUI** from the Start menu (or run the MSI again) and choose **Remove** in the wizard. **Settings → Apps → Installed apps** also works. Your mapping is retained in `%LOCALAPPDATA%\HPButtonRemapGUI\config.json` for reinstall.

The release workflow builds the MSI from `installer/Package.wxs` with WiX through `installer/build-msi.ps1`, which also generates the wizard's license page from `LICENSE`. The version comes from `<Version>` in `HPButtonRemap/HPButtonRemap.csproj`; pushing a matching `v*.*.*` tag publishes a release.

## Tests

```powershell
dotnet test .\tests\HPButtonRemap.Tests\HPButtonRemap.Tests.csproj
```

## Reliability notes

If the HP key service is not ready when the app starts (common right after sign-in), or it restarts later, the app keeps retrying in the background and shows a notification when the key is ready again. To update the standalone exe, exit the running copy from the tray first; the new exe tells you if it is still running.

## Testing, shortcuts, and pausing

Use **Test action** in Settings to try a target without saving it. For a keyboard shortcut, enter a combination such as `Win+Shift+S` or `Ctrl+Alt+N`. After clicking **Test action**, switch to the window that should receive the shortcut within three seconds. Windows may block simulated input in elevated apps.

Use **Pause remapping** in the tray menu to temporarily ignore the HP key. The current mapping is kept, and remapping resumes when you uncheck the item or restart the app. Opening a second copy reopens Settings in the running copy. If a launch fails or its target is missing, the tray app shows a Windows notification. If a manual config edit is invalid, reloading keeps the current mapping.

## Toggle an app window

Choose **Open an app or file** and select the app executable or a Windows shortcut that points to it. Press the HP key to launch the app when it is closed, bring an existing window forward, or minimize it when focused. Documents and websites still open normally. Choose the executable that owns the window if the app uses a separate launcher. Some tray apps hide their UI in ways Windows cannot restore from another process. If a matching process has no restorable window, HP Button Remap shows a notification and skips launching a second copy. Apps that run as administrator, such as WireGuard, are minimized and restored the same way the taskbar does it, so they toggle like any other app.
