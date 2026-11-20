# HP Button Remap

Lightweight tray application for remapping HP laptop special function keys (MyHP/HP System Event Utility/HP Programmable Key). Tested on an HP OmniBook, YMMV on other models. This is a vibe coded app, use at your own risk.

## Features

- run program
- run command
- open URL

## Requirements

- Windows 10/11
- HP device exposing `root\wmi` `hpqBEvnt` events
- .NET 8 Desktop Runtime

## Install

1. Download `HPButtonRemap.exe`
2. Run it once

It self-installs to `%LOCALAPPDATA%\HPButtonRemap\`.

## Uninstall

Tray icon -> **Uninstall...**

## Configuration

Edit `%LOCALAPPDATA%\HPButtonRemap\config.json` and click tray menu -> **Reload Configuration**.

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

Run the Windows x64 MSI and open **HP Button Remap GUI** from Start. Installation is per-user, includes the required .NET runtime, and leaves the hotkey unassigned and login startup disabled on first launch. To uninstall, exit the tray app and remove **HP Button Remap GUI** from **Settings → Apps → Installed apps**. Your mapping is retained in `%LOCALAPPDATA%\HPButtonRemapGUI\config.json` for reinstall.

The release workflow builds the MSI from `installer/Package.wxs` with WiX.
