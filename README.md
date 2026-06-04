# Voice to Text

A local, offline voice-to-text (dictation) app for Windows 11. It lives in the
system tray; a global hotkey toggles dictation, audio from the selected
microphone is transcribed locally with Whisper, and the text is typed into
whatever window has focus. Everything runs on-device — no cloud, no API keys,
and your audio never leaves your PC.

## Install

Download the latest `VoiceToText-Setup-x.y.z.exe` from
**[Releases](../../releases/latest)** and run it (per-user install, no admin
needed). A first-run wizard picks your microphone and hotkey; the speech model
(~1.6 GB) downloads once and everything is offline from then on. Updates are
offered automatically from this repository's releases when enabled in Settings.

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
- Press your hotkey to dictate — **press-to-toggle** or **hold-to-talk**
  (pick in Settings). Press-to-toggle can **auto-stop after a pause**; the
  transcription is pasted at your cursor.
- Double-click the tray icon for the dashboard: usage stats, Settings
  (microphone, speech model, hotkey, activation), **Text rules**
  (find→replace corrections + spoken commands like "new line"), opt-in
  dictation **History**, and About/diagnostics.
- The hotkey can be a single key (e.g. an extra/macro key or `F13`) or a
  modifier combo. Esc/Tab/Enter stay reserved for the dialog.
- Tray menu → **Check for updates…** checks the configured update source now.

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

The app updates itself from a configurable **update source** — by default this
repository's GitHub Releases
(`https://github.com/lukeeexd/voice-to-text/releases/latest/download`), but a
local/UNC **folder** works too (handy for development). Enable it in Settings
(off by default). On startup and via tray → **Check for updates…**, the app
reads `latest.json` from the source; if it names a newer version it offers to
install it, downloads/copies the setup to a local staging dir (verifying its
SHA-256), and a small relauncher runs the installer and reopens the app. Your
settings and model in `%APPDATA%\VoiceToText` are untouched.

> ⚠️ The app runs the installer the source provides, so only use a source you
> control and trust. The installer isn't code-signed yet (see roadmap), so the
> feature is off by default and shows a one-time consent prompt when enabled.

### Publishing a release

```powershell
# bump <Version> in src\VoiceToText\VoiceToText.csproj, then:
.\publish.ps1
iscc installer\VoiceToText.iss
# rename the setup with its version and compute its hash, write latest.json, then:
gh release create v<ver> VoiceToText-Setup-<ver>.exe latest.json --title "v<ver>" --notes "What changed"
```

`latest.json` (uploaded as a release asset next to the setup):

```json
{
  "Version": "0.9.0",
  "SetupFileName": "VoiceToText-Setup-0.9.0.exe",
  "Sha256": "<lowercase sha256 of the setup exe>",
  "ReleaseNotes": "What changed"
}
```

(`SetupFileName` must be a plain file name living next to the manifest — the
updater rejects paths.)

## Known limitations

- **Elevated windows:** Windows UIPI blocks both the hotkey and paste while an
  admin-elevated window is focused. Dictation works in normal apps (Word,
  Outlook, browsers) but not into admin-elevated windows.
- Dictation uses a single transcription pass on stop (push-to-talk / toggle),
  not live streaming.

## Roadmap

- Upgrade auto-stop to Silero VAD (more robust against background noise than the
  current energy-based detector)
- Live/streaming partial results
- Code-sign the installer + verify Authenticode/publisher in the updater (so
  auto-update trusts the binary, not just a manifest hash)

## License

[MIT](LICENSE)
