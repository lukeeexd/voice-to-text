---
name: smoke
description: Run VoiceToText's full self-test battery (the repo has no unit-test project - the exe self-tests via CLI flags). Use to verify changes, before any release, or when asked to run tests, smoke-test, or verify the build.
---

# Smoke-test VoiceToText

1. **Clean build first** (warnings hide under incremental builds):

   ```powershell
   dotnet build src/VoiceToText/VoiceToText.csproj --no-incremental
   ```

   (`dotnet` is per-user at `~/.dotnet`; set `DOTNET_ROOT` if needed.) Treat ANY
   warning — e.g. WFO1000 — as a failure to fix, not noise.

2. **Run the battery** from the repo root using the Debug exe
   `src\VoiceToText\bin\Debug\net10.0-windows\VoiceToText.exe`. Each mode writes
   `<name>-output.txt` to the CWD and exits 0 (pass) / 1 (fail):

   - Headless: `--vadtest`, `--statstest`, `--dashtest`, `--historytest`,
     `--textrulestest`, `--logtest`, `--abouttest`, `--widgettest`,
     `--updatecheck` (**NO argument — ever**)
   - GUI smoke (needs a desktop session, no mic/GPU): `--dashwindow` — constructs and
     paints every Dashboard page + the onboarding wizard; catches ctor/paint crashes
     that code review misses.

3. **Check**: every exit code is 0. For any failure, read that `*-output.txt` and
   report the failure verbatim — do not summarize it away.

4. **Full STT check** (only when STT/GPU/model code changed; downloads a ~1.6 GB
   model on first run): `--selftest <path-to-16k-mono.wav> selftest-output.txt` —
   verify the transcript looks right and that the `Vulkan` runtime loaded.

Hard rule: never pass a folder argument to `--updatecheck` — that mode recursively
deletes the folder it is given. The no-arg form is the only safe one.
