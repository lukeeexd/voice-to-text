# VoiceToText — project notes for Claude Code

Dictation tray app. C#/.NET 10, Whisper.net (Vulkan), shared engine in
`src/VoiceToText.Core`; two heads: `src/VoiceToText` (Windows 11, WinForms +
NAudio) and `src/VoiceToText.Linux` (Avalonia 11.3 + libpulse/X11/portals;
ships as an AppImage). Solution: `VoiceToText.slnx`.
Linux releases are GATED until the phase-4 VM validation pass
(see docs/superpowers/specs/2026-06-12-linux-port-design.md).

## Build & run

- The .NET 10 SDK is per-user at `~/.dotnet`. If `dotnet` isn't on PATH, use
  `$env:USERPROFILE\.dotnet\dotnet.exe` and set `DOTNET_ROOT=$env:USERPROFILE\.dotnet`.
- Build: `dotnet build` from the repo root. **Incremental builds hide warnings** —
  verify with `--no-incremental` (WFO1000 and friends only surface on clean builds).
- Run: `dotnet run --project src/VoiceToText`.

## Verification — there is no unit-test project; the exe self-tests

Run the built exe with these flags (each writes `<name>-output.txt` to the CWD,
exit 0 = pass / 1 = fail). Use `/smoke` to run the whole battery.

- Headless: `--vadtest` `--statstest` `--dashtest` `--historytest` `--textrulestest`
  `--logtest` `--abouttest` `--widgettest` `--controllertest` `--updatecheck`
  (**no argument — ever**)
- Linux head (`voicetotext` exe, also runs on Windows for the portable flags):
  the same battery minus the WinForms tests, plus `--audiotest` (needs PulseAudio;
  skips cleanly without) and `--uitest` (Avalonia.Headless). CI
  (`.github/workflows/linux.yml`) runs it all on ubuntu including a real-pulse
  capture roundtrip and a CPU tiny-model STT end-to-end.
- GUI smoke (desktop session, no mic/GPU): `--dashwindow` — constructs and paints all
  Dashboard pages + onboarding; catches ctor/paint crashes that code review misses.
- Full STT (GPU, downloads the model): `--selftest <16k-mono.wav> <out.txt>`

## ⚠️ Footguns

- **NEVER pass a real folder to `--updatecheck`.** It overwrites `latest.json` with a
  dummy and then `Directory.Delete()`s the folder recursively (`SelfTest.RunUpdateCheck`).
  The no-arg form self-tests safely in `%TEMP%`. A PreToolUse hook enforces this — do
  not work around it.
- There are TWO auto-update feeds: **GitHub Releases is canonical** (users' apps poll
  `https://github.com/lukeeexd/voice-to-text/releases/latest/download`); the local
  `D:\ClaudeCode\VoiceToText-Releases` folder (outside the repo) is the dev feed.
  `/release` ships to both. Verify with `/release`'s verify step or the
  `release-verifier` agent — never with `--updatecheck`.
- `iscc` must run **after** `publish.ps1` — the .iss reads its version from
  `publish\VoiceToText.exe`; a stale publish ships an old build.
- `AppMutex` in `installer/VoiceToText.iss` must stay identical to the mutex name in
  `Program.cs`, or the updater can't close the running app.
- `<Version>` in `src/Version.props` is the only version you edit (shared by both
  heads); `latest.json`'s versions are hand-written — keep them in sync
  (equal/lower versions silently never prompt).

## WinForms/GDI+ gotchas (this repo's recurring bug class)

- WFO1000 on public `Control` properties → add
  `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]`.
- Custom control ctors: setting `Size` (anything that triggers layout) before child
  fields are assigned crashes at runtime — `--dashwindow` is the smoke test for this.
- Disabled `TextBox` paints with the light default background (breaks the dark theme).
- Rounded `Region` + GDI+ fills leave corner specks — prefer anti-aliased fills.
- UIPI: the global hotkey and paste can't reach elevated windows; that's by design.

## Release & conventions

- After **any** version bump, ship to the feed without being asked — use `/release`.
- Commits: `feat(scope):` / `fix(scope):` / `style(scope):` / `docs:` …; release bumps
  use the literal form `vX.Y.Z: <summary>`.
