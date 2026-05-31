# Voice to Text

A local, offline voice-to-text (dictation) app for Windows 11. It lives in the
system tray; a global hotkey toggles dictation, audio from the selected
microphone is transcribed locally with Whisper, and the text is typed into
whatever window has focus. Everything runs on-device — no cloud, no API keys.

## Stack

| Concern | Choice |
|---|---|
| Language / shell | C# / .NET 10, WinForms tray host |
| Speech-to-text | [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp), model `large-v3-turbo` |
| GPU acceleration | Vulkan runtime (works on AMD/Intel/NVIDIA; no CUDA needed) |
| Audio capture | NAudio (WASAPI) → 16 kHz mono |
| Global hotkey | Win32 `RegisterHotKey` (toggle) |
| Text injection | Clipboard paste (`Ctrl+V` via `SendInput`) |

All core dependencies are MIT-licensed.

## Requirements

- Windows 11
- .NET 10 SDK
- A Vulkan-capable GPU (optional — falls back to CPU)

## Build & run

```powershell
dotnet build
dotnet run --project src/VoiceToText
```

On first launch the `large-v3-turbo` model (~1.6 GB) is downloaded once to
`%APPDATA%\VoiceToText\models`. After that it loads from disk.

### Usage

- The tray icon is **blue** (idle), **red** (recording), **amber** (transcribing).
- Press the hotkey (default **Ctrl + Shift + Space**) to start and speak. By
  default it **auto-stops after a pause** (configurable in Settings); you can
  also press the hotkey again to stop immediately. The transcription is then
  pasted at your cursor.
- Double-click the tray icon (or right-click → Settings) to pick a microphone,
  change the hotkey, toggle **start on login**, and enable **auto-update**.
- The hotkey can be a single key (e.g. an extra/macro key or `F13`) or a
  modifier combo. Esc/Tab/Enter stay reserved for the dialog.
- Tray menu → **Check for updates…** checks the configured update folder now.

### Verify the engine (no mic needed)

```powershell
dotnet run --project src/VoiceToText -- --selftest path\to\16k-mono.wav selftest-output.txt
```

Writes the transcript, timing, and which native runtime loaded (e.g. `Vulkan`).

## Build a standalone .exe (no .NET install needed)

```powershell
.\publish.ps1
```

This produces a **self-contained** app in `publish\`:

```
publish\
├─ VoiceToText.exe      # ~50 MB — all managed code + the .NET runtime bundled
└─ runtimes\            # ~48 MB — Whisper native libs (Vulkan + CPU)
```

Double-click `VoiceToText.exe` on any Windows 11 x64 machine — no .NET, no
`DOTNET_ROOT`, nothing to install. **Keep the `runtimes\` folder next to the
exe** (Whisper loads its native libraries from there). To move/share the app,
copy the whole `publish\` folder (or zip it).

The speech model is still downloaded on first run to `%APPDATA%\VoiceToText\models`
(it is intentionally not bundled into the exe).

## Build the installer

A per-user installer (no admin needed; Start-menu shortcut, optional desktop
shortcut and start-on-login, clean uninstall) is defined in
`installer\VoiceToText.iss` (Inno Setup).

```powershell
.\publish.ps1                       # 1. produce publish\
iscc installer\VoiceToText.iss      # 2. compile -> installer\Output\VoiceToText-Setup.exe
```

If you don't have Inno Setup, its compiler ships in the `Tools.InnoSetup`
NuGet package (no install required) — see the `ISCC.exe` inside that package.
The installer version is read automatically from the published exe, so just bump
`<Version>` in the csproj.

## Auto-update

The app can update itself from a **folder you designate** (a local path or a
network/shared drive) — no server, nothing published online. Enable it in
Settings (off by default) and pick the folder. On startup and via tray →
**Check for updates…**, the app reads `latest.json` from that folder; if it names
a newer version it offers to install it, copies the setup locally (verifying
SHA-256 if provided), and a small relauncher runs the installer and reopens the
app. Your settings and model in `%APPDATA%\VoiceToText` are untouched.

> ⚠️ The app runs the installer it finds in that folder, so only use a folder you
> control and trust. The installer isn't code-signed yet (see roadmap), so the
> feature is off by default and shows a one-time consent prompt when enabled.

### Publishing an update to the feed

```powershell
# bump <Version> in src\VoiceToText\VoiceToText.csproj, then:
.\publish.ps1
iscc installer\VoiceToText.iss
# copy the setup into your feed folder and write the manifest:
Copy-Item installer\Output\VoiceToText-Setup.exe <feed>\VoiceToText-Setup-<ver>.exe
```

`<feed>\latest.json`:

```json
{
  "Version": "0.3.0",
  "SetupFileName": "VoiceToText-Setup-0.3.0.exe",
  "Sha256": "<sha256 of the setup exe>",
  "ReleaseNotes": "What changed"
}
```

(`SetupFileName` must be a plain file name living in the feed folder.)

## Known limitations

- **Elevated windows:** Windows UIPI blocks both the hotkey and paste while an
  admin-elevated window is focused. Dictation works in normal apps (Word,
  Outlook, browsers) but not into admin-elevated windows.
- Dictation uses a single transcription pass on stop (push-to-talk / toggle),
  not live streaming.

## Roadmap

- Upgrade auto-stop to Silero VAD (more robust against background noise than the
  current energy-based detector)
- Hold-to-talk mode
- Live/streaming partial results
- Model picker + downloader UI
- Code-sign the installer + verify Authenticode/publisher in the updater (so
  auto-update trusts the binary, not just a same-folder hash)
- Post-processing (punctuation/formatting, custom vocabulary, voice commands)
