# HP Button Remap Configuration Examples

All examples use the single-button config shape:

- root: `EventID`, `EventData`, `Action`
- no `ButtonActions` array

## RunCommand

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

## OpenWebsite

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "OpenWebsite",
    "OpenWebsite": "https://example.com"
  }
}
```

## OpenFile

Opens an app, document, or shortcut. For apps, pressing the key again brings the running window forward, or minimizes it when it is already focused. `FilePath` must be a full path.

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "OpenFile",
    "FilePath": "C:\\Windows\\System32\\notepad.exe"
  }
}
```

## SendShortcut

Modifiers are `Ctrl`, `Alt`, `Shift`, and `Win`. The last key can be a letter, a digit, `F1`–`F24`, or a named key such as `Enter`, `Space`, `Tab`, `Escape`, `Delete`, `Home`, `End`, `PageUp`, `PageDown`, an arrow key, or `PrintScreen`.

```json
{
  "ShowStartupNotification": true,
  "RunAtStartup": true,
  "EventID": 29,
  "EventData": 8616,
  "Action": {
    "Type": "SendShortcut",
    "Shortcut": "Win+Shift+S"
  }
}
```

## Optional action fields

- `DelayMs`: wait this many milliseconds before running the action.
- `CreateNewWindow` (RunCommand): `true` starts the command in its own window; `false` runs it hidden.
- `WorkingDirectory` (RunCommand): folder to start in. Environment variables such as `%userprofile%` are expanded.
