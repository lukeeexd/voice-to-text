# Linux Port — Phase 3: AppImage, Updater & Release Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A CI-built, battery-tested `VoiceToText-x86_64.AppImage`, a Linux self-updater on the existing `latest.json` feed, shared versioning across heads, and a `/release` flow that ships both platforms — everything ready so the first Linux beta can publish the moment phase 4's VM validation passes.

**Architecture:** AppImage assets live in `installer/appimage/` (desktop entry, icon, `build-appimage.sh`); CI builds the AppDir from the linux-x64 publish and packs it with `appimagetool` (static type-2 runtime, `--appimage-extract-and-run` on runners). The updater extends `UpdateManifest`/`UpdateChecker` with additive Linux fields + a pure `DecideLinux`, staged downloads reuse `UpdateService`'s SHA-checked staging, and self-replace writes over `$APPIMAGE`. Versioning unifies via `src/Version.props`.

**Spec:** `docs/superpowers/specs/2026-06-12-linux-port-design.md` (phase 3). Deviation: the spec sketched `LinuxUrl`; implemented as `LinuxFileName` to match the Windows `SetupFileName` + feed-prefix convention exactly.

---

### Task 1: Shared version (`src/Version.props`)

- [ ] Create `src/Version.props` holding `<Version>` (current: 0.8.14) + the InformationalVersion guard; import it from BOTH head csprojs; delete the version lines from `VoiceToText.csproj`. The Windows publish must still produce `ProductVersion` 0.8.14 (verify via `publish.ps1`) and the updater must still parse it.
- [ ] Update `CLAUDE.md` ("`<Version>` in the csproj" → "`src/Version.props`") and `.claude/skills/release/SKILL.md` step 1 accordingly.
- [ ] Build + Windows battery + commit.

### Task 2: AppImage assets + build script (`installer/appimage/`)

- [ ] `voicetotext.png` — 256px icon extracted once from `app.ico` (System.Drawing locally) and committed.
- [ ] `voicetotext.desktop`:

```ini
[Desktop Entry]
Type=Application
Name=VoiceToText
Comment=Local voice dictation (Whisper)
Exec=voicetotext
Icon=voicetotext
Terminal=false
Categories=Utility;Accessibility;
```

- [ ] `build-appimage.sh` (runs on Linux/CI): takes the publish dir, assembles `AppDir/` (`usr/bin/` with all publish files, `AppRun` → `usr/bin/voicetotext` symlink, desktop file + icon at AppDir root), fetches pinned `appimagetool-x86_64.AppImage` (continuous), runs it with `--appimage-extract-and-run`, emits `VoiceToText-x86_64.AppImage`.
- [ ] CI (`linux.yml`): new step after the battery — build the AppImage, then run `--vadtest`, `--controllertest`, `--uitest` **from inside the AppImage** (`./VoiceToText-x86_64.AppImage --appimage-extract-and-run --vadtest` etc.), upload it as a workflow artifact.
- [ ] Commit; CI green gate.

### Task 3: Updater — manifest fields, pure decide, self-replace

- [ ] `UpdateManifest`: additive `LinuxVersion`, `LinuxFileName`, `LinuxSha256` (nullable strings; parser already tolerant both ways — old Windows clients ignore them, new code tolerates their absence).
- [ ] `UpdateChecker.DecideLinux(...)` — same decision table as `Decide` but reading the Linux fields (safe-bare-filename rule included). Extend `CoreSelfTest.RunUpdateCheck` with DecideLinux cases (runs on every platform's battery).
- [ ] `UpdateService`: generalize the staging path — private `StageFileAsync(fileName, sha)` used by the existing `StageInstallerAsync` (surface unchanged) and new `StageLinuxAsync(manifest)`.
- [ ] Linux head `Platform/LinuxUpdater.cs`: check (honoring `AutoUpdateEnabled` + `UpdateSkippedVersion`) → stage → self-replace `$APPIMAGE` (write staged file over it via temp+rename in the same directory, `File.SetUnixFileMode` +x) → notification "Update installed — restart VoiceToText". No `$APPIMAGE` (dev runs) or unwritable location → notification with the download link only.
- [ ] Settings window: "Check for updates now" button + status label; startup check when `AutoUpdateEnabled`.
- [ ] Build, both batteries, commit.

### Task 4: Release pipeline

- [ ] `.github/workflows/release.yml`: `on: push: tags: ['v*']` (plus `workflow_dispatch`) → build AppImage (reuse `build-appimage.sh`), `gh release upload <tag> VoiceToText-x86_64.AppImage --clobber` (needs `permissions: contents: write`).
- [ ] `/release` skill update: after `gh release create`, poll for the AppImage asset (`gh release view --json assets`), download, SHA-256 it, and write `latest.json` WITH the Linux fields **only when shipping Linux** (gated until phase 4 passes — until then the manifest stays Windows-only); upload the final `latest.json` last.
- [ ] `release-verifier` agent doc: validate Linux fields when present (file exists in release assets, SHA matches).
- [ ] Commit. (Dry-run: trigger `workflow_dispatch` on main to prove the AppImage job works without tagging.)

### Task 5: Verification + docs

- [ ] Windows: clean build 0 warnings, 11-flag battery, `publish.ps1` ProductVersion check.
- [ ] CI: linux.yml green including in-AppImage battery; release.yml dry-run green with downloadable artifact.
- [ ] Memory + CLAUDE.md note: Linux release remains gated on phase 4 (VM validation).

## Self-review
- Spec phase-3 coverage: AppImage(static runtime) ✔, updater additive fields + $APPIMAGE self-replace ✔, /release integration + verifier ✔, version unification ✔, in-AppImage tests ✔. Release gating to phase 4 made explicit.
- No placeholders: scripts/fields fully named; decision table reuses the existing, battery-covered `Decide` semantics.
- Consistency: `LinuxFileName` naming matches `SetupFileName` convention; `StageInstallerAsync` surface unchanged for the Windows head.
