<div align="center">

<img src="src/Flick.App/Assets/flick.png" alt="Flick" width="96" />

# Flick

**Trigger custom actions, macros, and live window peeking on Windows 11 with mouse strokes, multi-finger touchpad gestures, keyboard chords, and acoustic knock patterns.**

[![Latest Release](https://img.shields.io/github/v/release/piyushagade/Flick?style=flat-square&logo=github)](https://github.com/piyushagade/Flick/releases/latest)
[![VirusTotal](https://img.shields.io/badge/VirusTotal-Scanned-10b981?style=flat-square&logo=virustotal&logoColor=white)](https://github.com/piyushagade/Flick/releases/latest)
[![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-0078D6?style=flat-square&logo=windows)](https://github.com/piyushagade/Flick/releases/latest)
[![.NET 8](https://img.shields.io/badge/.NET-8.0%20%7C%20WinUI%203-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-PolyForm%20Noncommercial%201.0.0-orange?style=flat-square)](LICENSE)

*Built by [Piyush Agade](https://app.piyushagade.xyz/flick) using .NET 8, WinUI 3, Win32 hooks, and Audio DSP.*

[Download Latest Release](https://github.com/piyushagade/Flick/releases/latest) · [Features](#features) · [Architecture](#architecture) · [Build & Run](#build--run) · [License](#license)

</div>

---

## Download

Grab the latest Windows `x64` release (`v1.4.0`) from [**GitHub Releases**](https://github.com/piyushagade/Flick/releases/latest):

| Package | File | Description |
| :--- | :--- | :--- |
| **Installer (Recommended)** | `Flick-Setup-<version>-win-x64.exe` | Inno Setup installer with Start Menu shortcut and optional startup registration |
| **Portable** | `Flick-Portable-<version>-win-x64.zip` | Self-contained archive — extract and run anywhere |
| **Checksums** | `SHA256SUMS.txt` | SHA-256 verification hashes for all release artifacts |

**Requirements:** Windows 10 (build 19041+) or Windows 11 (`x64`). Self-contained — no separate .NET or Windows App SDK installation needed.

> **Windows SmartScreen Note:** Flick is an independent, unsigned binary. Windows SmartScreen may display a warning on first run. You can verify the download against `SHA256SUMS.txt`, inspect the VirusTotal scan results attached to each release, or build directly from source below.

---

## Features

### Input Triggers
- **Mouse Gestures** — Recognize custom mouse strokes with configurable trigger buttons (`Right`, `Middle`, `XButton1`, `XButton2`), optional modifier key filters (`Ctrl`, `Alt`, `Shift`), stroke smoothing, and customizable trail & ripple overlays.
- **Precision Touchpad Gestures** — Multi-finger precision touchpad swipe, tap, and pinch gesture tracking.
- **Keyboard Chords** — Simultaneous, sequential, and modifier-only sequential chords (such as `Ctrl → Alt → Alt`) with configurable timing windows and keystroke suppression.
- **Acoustic Knock Patterns** — Microphone-based knock detection using real-time FFT spectral bands, surface calibration, and pattern matching.
- **Hot Corners** — Trigger actions when moving the cursor into screen corners.

### Sneak Window Portal
- **Live X-Ray Portal** — Peek through the currently active window with a feathered, cursor-following circular portal without Alt-Tabbing.
- **Single Window Lock or Composite Layout** — Lock onto one window underneath (cycle with **Middle-Click**, **`Tab`**, or **`Left/Right Arrow`**) or composite all behind windows together as they are laid out on your desktop.
- **Runtime Controls** — Resize the portal on the fly with the **Mouse Scroll Wheel** (or **`+` / `-`**) and dismiss with **`Esc`**.

### App Scopes, Targeting & Actions
- **App Scopes & Window Targeting** — Scope rules to specific applications, ignore processes (e.g., remote desktop tools), auto-pause on fullscreen, and target either the active window or the window under the cursor.
- **Rich Action Set** — Hotkeys & text snippets, mouse actions & window management (including **Sneak**), system & media controls, URLs & Windows intents, programs & PowerShell scripts, HTTP requests, IFTTT webhooks, and MQTT publishing.
- **System Tray & HUD** — Minimize to tray, start with Windows, and display an optional action HUD pill.

---

## Architecture

- **`src/Flick.Core`** — Shared recognition engines, audio DSP, keyboard chord state machine, rule matching, models, and profile storage (`.NET 8`).
- **`src/Flick.App`** — Fluent WinUI 3 desktop application, Win32 hooks, `Sneak` portal overlay, services, and UI (`net8.0-windows10.0.19041.0`, `x64`).
- **`tests/Flick.Core.Tests`** — xUnit unit tests for `Flick.Core`.

---

## Build & Run

```powershell
# Build Flick.App (x64 for WinUI 3)
dotnet build src/Flick.App/Flick.App.csproj -p:Platform=x64

# Run unit tests
dotnet test

# Launch Flick WinUI 3 app
dotnet run --project src/Flick.App/Flick.App.csproj -p:Platform=x64
```

---

## Releases & Versioning

Version metadata is managed centrally in [`Directory.Build.props`](Directory.Build.props) using Semantic Versioning (`MAJOR.MINOR.PATCH`).

The GitHub Actions workflow ([`.github/workflows/release.yml`](.github/workflows/release.yml)) automatically builds self-contained `win-x64` binaries, packages an Inno Setup Windows installer (`Flick-Setup-<version>-win-x64.exe`) and portable archive (`Flick-Portable-<version>-win-x64.zip`) with `SHA256SUMS.txt`, scans artifacts with VirusTotal (when `VIRUSTOTAL_API_KEY` is configured), and publishes a GitHub Release:

- **Tag-triggered release:**
  ```powershell
  git tag -a v1.3.1 -m "Release v1.3.1"
  git push origin v1.3.1
  ```
- **Manual release from GitHub Actions:** Go to **Actions → Release Windows Installer → Run workflow** and select `patch`, `minor`, `major`, or `custom` to automatically bump `Directory.Build.props`, commit, tag, build, and publish the release.

---

## License

Flick is licensed under the [**PolyForm Noncommercial License 1.0.0**](LICENSE). You are free to use, modify, and share Flick for personal and noncommercial purposes.

> Required Notice: Copyright © 2026 Piyush Agade (https://github.com/piyushagade/Flick)
