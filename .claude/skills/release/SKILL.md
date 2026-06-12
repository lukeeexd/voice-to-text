---
name: release
description: Ship a VoiceToText release end-to-end - bump <Version>, build the self-contained exe, compile the Inno Setup installer, publish to BOTH update feeds (GitHub Releases = canonical for users; D:\ClaudeCode\VoiceToText-Releases = local/dev) with a SHA-256 manifest, and verify. Use after any version bump or when asked to release, ship, or publish an update.
---

# Release VoiceToText

There are TWO feeds — publish to BOTH on every release:

- **GitHub Releases** (canonical — what users' apps poll):
  `https://github.com/lukeeexd/voice-to-text/releases/latest/download` serves the
  newest release's assets (`latest.json` + the setup exe).
- **Local feed** (dev/test): `D:\ClaudeCode\VoiceToText-Releases`.

Standing instruction: after ANY version bump, ship — do not wait to be asked.

## Steps (order matters)

1. **Bump version** — edit `<Version>` in `src/Version.props` (shared by the
   Windows and Linux heads; plain SemVer, no `+suffix`; `InformationalVersion`
   must stay parseable by the updater). Skip if already bumped for this release.
2. **Publish** — run `.\publish.ps1` from the repo root. It wipes and rebuilds
   `publish\` (self-contained win-x64 single file + `runtimes\`).
3. **Installer** — run `iscc installer\VoiceToText.iss` ONLY AFTER step 2 (the .iss
   reads its version from `publish\VoiceToText.exe`; a stale publish ships an old
   build). If `iscc` is not on PATH, use `ISCC.exe` from the `Tools.InnoSetup` NuGet
   package. Output: `installer\Output\VoiceToText-Setup.exe`.
4. **Copy to feed** —
   `Copy-Item installer\Output\VoiceToText-Setup.exe D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-<ver>.exe`
5. **Hash** — `(Get-FileHash <feed-exe> -Algorithm SHA256).Hash.ToLowerInvariant()`.
   Recompute EVERY time the setup is rebuilt — Inno output is not byte-reproducible.
6. **Manifest** — write `D:\ClaudeCode\VoiceToText-Releases\latest.json`:

   ```json
   {
     "Version": "<ver>",
     "SetupFileName": "VoiceToText-Setup-<ver>.exe",
     "Sha256": "<lowercase hash>",
     "ReleaseNotes": "<what changed>"
   }
   ```

   `SetupFileName` must be a bare filename (no path separators — the updater rejects
   anything else). `Version` must exactly match the Version.props bump; this is the
   only hand-typed copy of the version and the #1 drift point.
7. **GitHub release** (canonical feed) — from the repo root:

   ```powershell
   gh release create v<ver> "D:\ClaudeCode\VoiceToText-Releases\VoiceToText-Setup-<ver>.exe" `
     "D:\ClaudeCode\VoiceToText-Releases\latest.json" --title "v<ver>" --notes "<what changed>"
   ```

   Upload the SAME feed-named exe + the SAME latest.json written in step 6 (the
   manifest's `Sha256` must match the uploaded exe — never re-run iscc in between).
   Push `main` first if unpushed (`git push`).
8. **Verify** (never with `--updatecheck` against the feed!):
   - Parse `latest.json` back; `Version` == csproj `<Version>`; `SetupFileName` exists
     in the feed folder.
   - Recompute the SHA-256 of the feed exe and compare to the manifest.
   - GitHub side: `Invoke-WebRequest https://github.com/lukeeexd/voice-to-text/releases/latest/download/latest.json`
     parses and matches the local manifest (allow a minute for asset propagation).
   - Run the built app's no-arg self-test: `<exe> --updatecheck` (NO argument — it
     uses a `%TEMP%` feed) and check `updatecheck-output.txt` + exit code 0.
   - Optionally launch the `release-verifier` agent for an independent check.
9. **Commit** — `vX.Y.Z: <summary>` per repo convention, and `git push`.

## Linux (AppImage) — ⛔ GATED until phase-4 VM validation passes

Do NOT add the Linux fields to `latest.json` before the phase-4 gate clears
(see `docs/superpowers/specs/2026-06-12-linux-port-design.md`); publishing them
turns on the Linux self-updater for anyone running the AppImage. Once cleared,
between steps 7 and 8:

L1. Tagging the release (step 7) triggers `.github/workflows/release.yml`,
    which builds `VoiceToText-x86_64.AppImage`, smoke-tests inside it, and
    attaches it to the same release. Wait: `gh run watch --exit-status` on the
    `release` workflow run.
L2. Download it back and hash it:
    `gh release download v<ver> --pattern VoiceToText-x86_64.AppImage --dir $env:TEMP\vtt-rel`
    then SHA-256 it (lowercase).
L3. Rewrite `latest.json` ADDING (keep the Windows fields intact):
    `"LinuxVersion": "<ver>", "LinuxFileName": "VoiceToText-x86_64.AppImage", "LinuxSha256": "<hash>"`
L4. Replace the release's manifest: `gh release upload v<ver> latest.json --clobber`,
    and copy the AppImage + updated latest.json to the local feed folder too.
L5. Verify (additional to step 8): the release's asset list contains the
    AppImage; re-download `latest.json` from `releases/latest/download` and check
    the Linux trio parses and the SHA matches the downloaded AppImage. First
    Linux release notes must say **beta**.

## Hard rules

- NEVER pass any real folder (especially the feed) to `--updatecheck`: it overwrites
  `latest.json` with a dummy and recursively DELETES the folder
  (`SelfTest.RunUpdateCheck`). A PreToolUse hook blocks this — do not work around it.
- The updater only offers strictly-greater versions; forgetting either side of the
  bump (csproj or manifest) fails silently with no prompt.
- Don't flip `AutoUpdateEnabled` defaults and don't touch the `AppMutex` name (must
  match `Program.cs`).
