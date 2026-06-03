# PreToolUse guard: the app's --updatecheck self-test writes dummy files into the
# folder it is given and then Directory.Delete()s that folder RECURSIVELY
# (SelfTest.RunUpdateCheck). Only the no-argument form (which uses
# %TEMP%\vtt-updatetest-feed) is safe. This hook denies anything else.
$raw = [Console]::In.ReadToEnd()
try { $cmd = ($raw | ConvertFrom-Json).tool_input.command } catch { exit 0 }
if (-not $cmd -or $cmd -notmatch '--updatecheck') { exit 0 }

function Deny([string]$why) {
    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $why
        }
    } | ConvertTo-Json -Compress -Depth 4 | Write-Output
    exit 0
}

if ($cmd -match 'VoiceToText-Releases') {
    Deny ('BLOCKED: --updatecheck together with the real update feed (VoiceToText-Releases). ' +
        'It would overwrite latest.json with a dummy and recursively DELETE the feed folder. ' +
        'Run --updatecheck with NO argument (it self-tests in %TEMP%\vtt-updatetest-feed) and ' +
        'verify the feed with Get-FileHash / the release-verifier agent instead.')
}

# --updatecheck followed by a folder argument that is not an obvious temp path
if ($cmd -match '--updatecheck\s+["'']?(?<arg>[^\s"''>|;&]+)') {
    if ($Matches['arg'] -notmatch 'te?mp') {
        Deny ("BLOCKED: --updatecheck '" + $Matches['arg'] + "' - this self-test mode overwrites and then " +
            'recursively DELETES the folder it is given. Run it with NO argument to self-test safely in %TEMP%.')
    }
}
exit 0
