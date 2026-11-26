# Flick

**Flick for Windows 11** (`v1.3.0`) — Trigger custom actions and macros with mouse strokes, multi-finger touchpad gestures, keyboard chords, and acoustic knock patterns.

Built by [Piyush Agade](https://app.piyushagade.xyz/flick) using .NET 8, WinUI 3, Win32 hooks, and Audio DSP.

## Features

- **Mouse Gestures**: Recognize custom mouse strokes with configurable trigger buttons (`Right`, `Middle`, `XButton1`, `XButton2`), optional modifier key filters (`Ctrl`, `Alt`, `Shift`), stroke smoothing, and customizable trail & ripple overlays.
- **Precision Touchpad Gestures**: Multi-finger precision touchpad gesture tracking.
- **Keyboard Chords**: Simultaneous and sequential keyboard chord recognition with configurable timing windows.
- **Acoustic Knock Patterns**: Microphone-based knock detection using FFT feature extraction, surface classification, and pattern matching.
- **Hot Corners**: Trigger actions when moving the cursor into screen corners.
- **App Scopes & Window Targeting**: Scope rules to specific applications, ignore processes (e.g., remote desktop tools), auto-pause on fullscreen, and target either the active window or the window under the cursor.
- **Rich Action Set**:
  - Hotkeys & text snippets
  - Mouse actions & window management
  - System actions & media controls
  - Open URLs & Windows intents
  - Run programs & PowerShell scripts
  - HTTP requests, IFTTT webhooks, and MQTT publishing
- **System Tray & HUD**: Minimize to tray, start with Windows, and display an optional action HUD pill.

## Project Structure

```text
Flick.sln
├── src/
│   ├── Flick.Core/          # Recognition engines, audio DSP, rule matching, models, and profile storage (.NET 8)
│   └── Flick.App/           # WinUI 3 desktop application, Win32 hooks, services, and UI (net8.0-windows10.0.19041.0, x64)
└── tests/
    └── Flick.Core.Tests/    # xUnit unit tests for Flick.Core
```

## Building & Running

### Prerequisites

- Windows 11 (x64) (or Windows 10 1809+ / SDK `10.0.19041.0`)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (with Windows App SDK / WinUI 3 workload) or the Windows App SDK build tools

### Commands

Run the unit tests:

```powershell
dotnet test
```

Build the solution (x64):

```powershell
dotnet build Flick.sln -p:Platform=x64
```

You can also open `Flick.sln` in Visual Studio 2022 and run `Flick.App` directly.

## License

This project is licensed under the **PolyForm Noncommercial License 1.0.0** — see the [LICENSE](LICENSE) file for details.
