# GitHub Update Distribution — Design Spec

**Date:** 2026-06-04
**Status:** Approved
**Ships as:** v0.8.9

Distribute the app + updates via GitHub so other people can install and auto-update.
Decisions (user-approved): one **public** repo `github.com/lukeeexd/voice-to-text` holding source
**and** releases; the GitHub feed becomes canonical for everyone; the local-folder feed stays
supported as a dev/test path.

## How the feed maps to GitHub

GitHub Releases provides a stable alias for the newest release's assets:
`https://github.com/lukeeexd/voice-to-text/releases/latest/download/<file>`.
Each release `vX.Y.Z` uploads two assets: `VoiceToText-Setup-X.Y.Z.exe` and `latest.json`
(identical manifest to today). The app treats the URL prefix exactly like a feed folder:
manifest at `<feed>/latest.json`, setup at `<feed>/<SetupFileName>`. The existing SHA-256
verification, safe-filename rule, staging, and relauncher are unchanged.

## Code changes

1. **`Update/UpdateService.cs`**
   - `public static bool IsHttpFeed(string? feed)` — true when the trimmed value starts with
     `http://` or `https://` (OrdinalIgnoreCase).
   - A single `private static readonly HttpClient` with a `User-Agent: VoiceToText-Updater`
     header (GitHub rejects UA-less requests); redirects followed (default), per-call timeout
     via linked CTS like the existing folder path.
   - `CheckAsync`: HTTP feed → GET `{feed.TrimEnd('/')}/latest.json` (10 s bound), parse via
     `UpdateManifest.TryParse`; network failures map to `ManifestInvalid` with the message,
     mirroring the folder error handling. Folder behavior byte-for-byte unchanged.
   - `StageInstallerAsync`: HTTP feed → stream `{feed}/{SetupFileName}` to the existing
     `.part` file in `StagingDir`, then the same SHA-256 check + rename. Folder path unchanged.
2. **`Update/UpdateChecker.cs`** — unchanged (pure logic already feed-agnostic; the
   bare-filename rule composes safely with URL joining).
3. **`Settings/AppSettings.cs`** — `UpdateFeedFolder` default `""` →
   `"https://github.com/lukeeexd/voice-to-text/releases/latest/download"`; doc comment says
   folder (local/UNC) **or** https feed URL. `AutoUpdateEnabled` stays default-off; the consent
   flow is untouched (new installs are pre-pointed at GitHub but updating remains opt-in).
4. **`Dashboard/SettingsPage.cs`** — row label "Update folder" → "Update source"; warning
   caption → covers folder *or URL*; Browse button kept for folder selection.
5. **`Diagnostics/SelfTest.cs`** (`RunUpdateCheck`, NO network calls in tests): add pure
   checks for `IsHttpFeed` (https/http/UNC/local/empty) and manifest-URL join shape. The
   existing %TEMP% folder-feed tests stay exactly as they are.

## Repo + pipeline

6. **Public repo prep:** add `LICENSE` (MIT, Luke Madigan) and a `README.md` (what the app is,
   fully-local promise, download link to `releases/latest`, build/self-test notes). Create
   `lukeeexd/voice-to-text` public and push `main`.
7. **Release skill** (`.claude/skills/release/SKILL.md`): after the local-feed publish, add
   `gh release create v<ver> <feed setup exe> <feed latest.json> --title v<ver> --notes <notes>`.
   GitHub is the canonical feed; the local folder remains for dev. CLAUDE.md updated to match.
8. **Bootstrap order:** v0.8.9 ships to the LOCAL feed first (installed v0.8.8 only understands
   folders) *and* to GitHub. After updating, the user pastes the GitHub URL into
   Settings → Update source (no auto-migration code — single-user concern).

## Security / privacy

- HTTPS + the manifest's pinned SHA-256 keeps installer integrity end-to-end.
- The update check is the app's only network call, remains opt-in (consent + toggle), and the
  audio/transcript never-leaves-this-PC promise is unaffected.

## Verification

Clean `--no-incremental` build 0/0; full smoke battery green (`--updatecheck` no-arg only);
manual: a real HTTP check against the published GitHub release returns UpToDate for v0.8.9.
