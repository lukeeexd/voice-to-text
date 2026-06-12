# Linux Port & AppImage Installer — Design

**Date:** 2026-06-12
**Status:** Approved (brainstorming complete)
**Constraint:** No physical Linux device available — correctness must come from
design conservatism, CI on real Linux runners, WSL2, and a Hyper-V VM.

## Goal

Ship VoiceToText for Linux as a **lean dictation port**: global hotkey →
record → Whisper transcription → text rules → paste, with tray presence, a
minimal settings window, autostart, and auto-update. Distributed as a single
**AppImage**. The full Windows dashboard (stats charts, history browser,
onboarding wizard, floating widget) is explicitly out of scope for v1.

The Windows app's behavior must be preserved exactly — verified by the
existing self-test battery before any Linux code ships.

## Decisions made during brainstorming

| Question | Decision |
|---|---|
| Scope | Lean dictation port (no dashboard) |
| Display servers | X11 **and** Wayland, with graceful degradation |
| Packaging | AppImage only |
| Verification | GitHub Actions CI + WSL2 inner loop + Hyper-V Ubuntu VM gate |
| Architecture | Shared-core refactor (option A) — one engine, two thin heads |

## Architecture

Three projects in `VoiceToText.slnx`:

```
src/VoiceToText.Core/      net10.0          — portable engine (NEW)
src/VoiceToText/           net10.0-windows  — existing WinForms head, refactored
                                              to reference Core; zero UX change
src/VoiceToText.Linux/     net10.0          — lean Avalonia head (NEW)
```

**Moves into Core** (already portable per the codebase audit): Whisper
transcription pipeline, model download manager, VAD, text rules, history,
stats, settings models + JSON persistence (storage paths injected), update-
check logic, sound-cue WAV resources, logging, self-test helpers.

**Platform service interfaces** (defined in Core, implemented per head):

| Interface | Windows (existing code) | Linux (new) |
|---|---|---|
| `IAudioCapture` | NAudio `WaveInEvent` | libpulse simple API (P/Invoke) |
| `ICuePlayer` | NAudio playback + volume | libpulse simple API, software gain |
| `IHotkeyService` | Win32 `RegisterHotKey` | 3-tier: XGrabKey / portal / IPC |
| `ITextInjector` | clipboard + `SendInput` Ctrl+V | clipboard + XTEST / portal |
| `IAutostart` | registry Run key | XDG `~/.config/autostart/*.desktop` |
| `IAppPaths` | `%APPDATA%` | XDG dirs (`~/.config`, `~/.local/share`) |
| `ISingleInstance` | named mutex | unix socket in `$XDG_RUNTIME_DIR` |

`ITextInjector` returns an outcome — `Pasted` or `CopiedToClipboardOnly` — so
the UI can notify the user when manual Ctrl+V is required.

Engine flow is identical on both OSes:
hotkey → capture → VAD → Whisper → text rules → inject → history/stats.

The Linux head is Avalonia 11 (X11 backend; runs under XWayland on Wayland
sessions — acceptable because hotkey/paste/tray go through D-Bus or our own
backends, not the toolkit).

## Linux backends

### Audio — libpulse "simple" API

P/Invoke binding to five functions (`pa_simple_new/read/write/drain/free`).
Capture 16 kHz mono S16LE (libpulse resamples); playback for sound cues with
software gain for the volume slider. Works on every modern desktop distro:
PipeWire ships `pipewire-pulse`, older distros run PulseAudio. No bundled
native audio library.

**Lean simplification:** v1 captures from the **system default source** — no
device picker. Users choose their mic in system sound settings (idiomatic on
Linux). This removes the entire async-libpulse enumeration API from scope.

### Hotkey — three tiers, auto-selected at startup

1. **X11 session** (`XDG_SESSION_TYPE=x11`): `XGrabKey` via libX11, dedicated
   event-loop thread. Exact Windows equivalent.
2. **Wayland + GlobalShortcuts portal** (KDE Plasma 5.27+, Hyprland):
   `org.freedesktop.portal.GlobalShortcuts` via Tmds.DBus. Detected by D-Bus
   introspection. **GNOME has not shipped this portal** (verified 2026-06),
   so it cannot be the primary Wayland path.
3. **Universal IPC trigger** (always available): invoking the app again with
   `--toggle` forwards the command over the single-instance unix socket and
   exits. Any DE's native keyboard-shortcut settings can bind a key to the
   AppImage path + `--toggle`. **On GNOME, first-run offers one-click
   auto-registration** by writing a custom shortcut via `gsettings`
   (`org.gnome.settings-daemon.plugins.media-keys`); other DEs get copy-paste
   instructions in the settings window.

The settings window displays which tier is active.

**Recording-mode note:** toggle mode works on all three tiers. Push-to-talk
needs key-down/key-up events, which only tier 1 (XGrabKey) and tier 2 (portal
`Activated`/`Deactivated` signals) provide — on tier 3 (IPC) the app exposes
toggle mode only and the settings window says why.

### Paste — clipboard-always, then best-effort injection

1. Transcript is **always written to the clipboard first** (matches Windows
   behavior; guarantees no dictation is ever lost).
2. X11 session: XTEST `XTestFakeKeyEvent` synthesizes Ctrl+V.
3. Wayland: `org.freedesktop.portal.RemoteDesktop` →
   `NotifyKeyboardKeycode`. One-time permission dialog; `restore_token`
   persisted in settings (persists across reboots on GNOME; KDE currently
   re-prompts after reboot — accepted papercut).
4. Injection unavailable/declined → outcome `CopiedToClipboardOnly` → desktop
   notification via `org.freedesktop.Notifications`: "Transcript copied —
   press Ctrl+V."

Known parity limitation: terminals expect Ctrl+Shift+V (same issue class
exists on Windows today).

### Tray & UI

- Avalonia `TrayIcon` → D-Bus StatusNotifierItem. Native on KDE/XFCE/
  Cinnamon; GNOME needs the AppIndicator extension (preinstalled on Ubuntu,
  not on Fedora).
- **The app must be fully usable without a tray**: `--settings` opens the
  settings window; recording state is conveyed by sound cues + tray icon
  swap. The floating recording widget is deferred.
- Settings window contents: model download/selection, language, hotkey setup
  (per-tier status + GNOME auto-setup button), cue volume, autostart toggle,
  force-CPU toggle, note that capture uses the system default mic.
- First-run dialog: model download prompt + hotkey setup.

### Whisper runtime

`RuntimeLibraryOrder = [Vulkan, Cpu]`. If `libvulkan.so.1` is absent or
Vulkan init fails, Whisper.net falls back to the bundled CPU runtime. A
Force-CPU setting guards against broken GPU drivers. The csproj's existing
`StripForeignNativeRuntimes` target confirms the NuGet packages ship
`linux-x64` and `vulkan/linux-x64` natives; the Linux publish keeps those
and strips the rest (inverse of today's target).

### Host requirements

x64 glibc desktop distro, PipeWire or PulseAudio, D-Bus session bus,
libX11/XWayland present (ubiquitous on desktops).

## Packaging — AppImage

- `dotnet publish -r linux-x64 --self-contained` of `VoiceToText.Linux`,
  native runtimes trimmed to `linux-x64` + `vulkan/linux-x64`.
- AppDir (`AppRun`, `.desktop`, icon) built with `appimagetool` using the
  **static type-2 runtime** (avoids the libfuse2 breakage on Ubuntu 22.04+).
- Built in GitHub Actions; WSL2 is the local fallback build environment.
- Expected size ~120–150 MB. Models still download on first run to
  `~/.local/share/VoiceToText`.

## Auto-update — one feed, simpler than Windows

- `latest.json` gains additive fields: `LinuxVersion`, `LinuxUrl`,
  `LinuxSha256`. Existing Windows clients must ignore unknown fields —
  verify against the current parser before shipping.
- Linux flow: check feed → download new AppImage to temp → SHA-256 verify →
  atomically replace own file (path from `$APPIMAGE`) → set exec bit →
  prompt restart. If own location is unwritable → download-and-notify.

## Release flow

- Windows side of `/release` unchanged (local `publish.ps1` + `iscc`).
- New GitHub Actions release workflow builds the AppImage from the pushed
  tag. `/release` waits for it (`gh run watch`), downloads the artifact,
  computes its SHA-256, writes the unified `latest.json`, and uploads all
  assets to the GitHub Release (canonical feed). Local dev feed keeps
  receiving Windows artifacts as today.
- `release-verifier` agent extended to validate the Linux fields.

## Verification plan

1. **Refactor safety (Windows):** after Core extraction, clean
   `--no-incremental` build + full `/smoke` battery + `--dashwindow` must
   pass; ship a Windows release to prove zero regression **before** Linux
   code lands.
2. **CI battery (every push):** ubuntu-22.04 + ubuntu-24.04 matrix —
   portable self-tests (`--vadtest --statstest --historytest
   --textrulestest --logtest`, now in Core), Avalonia **headless UI smoke**
   (`--linuxuitest`, the `--dashwindow` equivalent), and a full CPU STT
   `--selftest` with the cached tiny model. From phase 3 onward the tests
   execute **from inside the built AppImage** so packaging itself is under
   test (phase 2 runs them against the raw publish output).
3. **CI end-to-end mic test:** PulseAudio on the runner with a virtual
   microphone fed from a WAV file — exercises trigger → capture → VAD →
   Whisper → text rules → clipboard, headless, on real Linux.
4. **WSL2 inner loop:** real app under WSLg with mic passthrough for
   fast iteration (tray/hotkey not testable there).
5. **VM final gate:** Hyper-V Ubuntu 24.04, GNOME Wayland **and** Xorg
   sessions — tray icon, GNOME hotkey auto-registration, dictation into a
   real editor, portal permission dialog + reboot persistence, autostart,
   updater against a test feed, AppImage double-click launch.
6. **Adversarial review of every P/Invoke signature** — marshaling bugs are
   the classic unreproducible-without-hardware failure; each `DllImport` is
   reviewed and CI-exercised.

## Residual risks (accepted)

- KDE/XFCE/niche DEs ship verified by design tiers + CI only, not hands-on →
  first Linux release is labeled **beta** in release notes.
- Clipboard-always rule guarantees no lost dictations even when a hotkey or
  injection tier misbehaves.
- Vulkan-on-Linux driver variance → CPU fallback + Force-CPU setting.

## Implementation phases (each independently verifiable)

1. **Core extraction** + Windows re-verification + Windows release.
2. **Linux head**: platform backends, lean UI, self-tests, CI green.
3. **AppImage packaging** + updater + release-pipeline integration.
4. **WSL2/VM validation pass** → first Linux beta release.
