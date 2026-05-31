using System.Reflection;
using System.Security.Cryptography;
using VoiceToText.Settings;

namespace VoiceToText.Update;

/// <summary>
/// Impure orchestrator around the pure <see cref="UpdateChecker"/>: reads the manifest
/// from the feed folder, stages (copies + integrity-checks) the installer locally, and
/// writes the relauncher shim that survives this process so the app always comes back.
/// </summary>
public sealed class UpdateService(AppSettings settings)
{
    public static string StagingDir => Path.Combine(Path.GetTempPath(), "VoiceToText-Update");
    public static string UpdateLogPath => Path.Combine(StagingDir, "VoiceToText-Update.log");

    /// <summary>Current running version, or null if it can't be determined (updates then stay off).</summary>
    public Version? CurrentVersion { get; } = ResolveCurrentVersion();

    private static Version? ResolveCurrentVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            var asmVersion = asm?.GetName().Version;
            if (asmVersion is not null && asmVersion != new Version(0, 0, 0, 0))
                return asmVersion;

            // Fallback to the informational version attribute.
            var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return VersionParsing.TryNormalize(info);
        }
        catch
        {
            return null;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.AutoUpdateEnabled || string.IsNullOrWhiteSpace(settings.UpdateFeedFolder))
            return UpdateChecker.Decide(settings.AutoUpdateEnabled, settings.UpdateFeedFolder, CurrentVersion, null);

        try
        {
            var manifestPath = Path.Combine(settings.UpdateFeedFolder, UpdateManifest.ManifestFileName);
            if (!File.Exists(manifestPath))
                return new(UpdateDecision.ManifestInvalid, CurrentVersion, null, null, "No update manifest found in the update folder.");

            // Bound the read so a hung/half-mounted network share fails fast.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var json = await File.ReadAllTextAsync(manifestPath, cts.Token).ConfigureAwait(false);

            var manifest = UpdateManifest.TryParse(json);
            return UpdateChecker.Decide(true, settings.UpdateFeedFolder, CurrentVersion, manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or OperationCanceledException or NotSupportedException or ArgumentException)
        {
            return new(UpdateDecision.ManifestInvalid, CurrentVersion, null, null, $"Could not read the update folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy the setup exe from the feed folder to a local temp file (never run it straight
    /// off a network share), verify its SHA-256 if the manifest provides one, and return
    /// the staged path. Cleans up partial files on failure.
    /// </summary>
    public async Task<string> StageInstallerAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(settings.UpdateFeedFolder, manifest.SetupFileName!);
        Directory.CreateDirectory(StagingDir);
        var dest = Path.Combine(StagingDir, manifest.SetupFileName!);
        var part = dest + ".part";

        try
        {
            if (File.Exists(part)) File.Delete(part);

            await using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var actual = await ComputeSha256Async(part, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, manifest.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded update failed its integrity check (SHA-256 mismatch).");
            }

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
            return dest;
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { /* best effort */ }
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha.ComputeHashAsync(fs, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Write the self-deleting batch shim that outlives this process: it waits for our PID
    /// to exit (so all file/native-DLL locks release), runs the installer silently, then
    /// relaunches the app (passing the target version so it can confirm success), and finally
    /// deletes the staged setup and itself.
    /// </summary>
    public string WriteRelauncherShim()
    {
        Directory.CreateDirectory(StagingDir);
        var shimPath = Path.Combine(StagingDir, "relaunch.cmd");
        const string script = """
            @echo off
            setlocal
            set "APPPID=%~1"
            set "SETUP=%~2"
            set "APPEXE=%~3"
            set "LOGFILE=%~4"
            set "TARGETVER=%~5"
            set /a tries=0
            :waitloop
            tasklist /FI "PID eq %APPPID%" 2>nul | find /I "VoiceToText.exe" >nul
            if errorlevel 1 goto runsetup
            set /a tries+=1
            if %tries% geq 30 goto runsetup
            timeout /t 1 /nobreak >nul
            goto waitloop
            :runsetup
            "%SETUP%" /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /LOG="%LOGFILE%"
            start "" "%APPEXE%" --postupdate "%TARGETVER%"
            del /q "%SETUP%" >nul 2>&1
            endlocal
            (goto) 2>nul & del "%~f0"
            """;
        File.WriteAllText(shimPath, script);
        return shimPath;
    }

    /// <summary>Best-effort cleanup of leftover staged files (call only on a normal, non-post-update start).</summary>
    public static void CleanStaging()
    {
        try
        {
            if (Directory.Exists(StagingDir))
                Directory.Delete(StagingDir, recursive: true);
        }
        catch
        {
            // A shim may still be finishing; ignore and let the next start clean up.
        }
    }
}
