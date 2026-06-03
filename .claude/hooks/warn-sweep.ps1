# Stop hook: incremental builds hide warnings (WFO1000 etc.). When .cs files are
# dirty, run ONE clean build per unique change-set and block the stop with any
# warnings/errors found so they get fixed before being committed.
$raw = [Console]::In.ReadToEnd()
$stopActive = $false
try { $stopActive = [bool]($raw | ConvertFrom-Json).stop_hook_active } catch {}

$repo = $env:CLAUDE_PROJECT_DIR
if (-not $repo) { $repo = (Get-Location).Path }

$porc = (git -C $repo status --porcelain -- '*.cs' | Out-String).Trim()
if (-not $porc) { exit 0 }

# One sweep per unique change-set: hash the diff + status + untracked-file stamps.
$diff = (git -C $repo diff HEAD -- '*.cs' | Out-String)
$stamps = (git -C $repo ls-files --others --exclude-standard -- '*.cs') | ForEach-Object {
    $p = Join-Path $repo $_
    if (Test-Path $p) { (Get-Item $p).LastWriteTimeUtc.Ticks }
}
$md5 = [System.Security.Cryptography.MD5]::Create()
$state = [BitConverter]::ToString($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($porc + $diff + ($stamps -join ','))))
$marker = Join-Path $env:TEMP 'vtt-warn-sweep.hash'
if ((Test-Path $marker) -and ((Get-Content $marker -ErrorAction SilentlyContinue) -eq $state)) { exit 0 }
# Write the marker BEFORE building so an identical change-set never re-blocks (no stop loops).
Set-Content -Path $marker -Value $state -Encoding ascii

$env:DOTNET_ROOT = Join-Path $env:USERPROFILE '.dotnet'
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$out = & $dotnet build (Join-Path $repo 'src\VoiceToText\VoiceToText.csproj') --no-incremental -nologo -v:m 2>&1 |
    ForEach-Object { "$_" }

$findings = $out | Where-Object { $_ -match '\b(warning|error)\s+\w*\d+:' } | Select-Object -Unique
if ($findings) {
    $msg = "Clean-build sweep (--no-incremental) found issues that incremental builds hide:`n" +
        (($findings | Select-Object -First 20) -join "`n")
    if ($stopActive) {
        # Already continuing from a Stop-hook block - inform, never loop.
        @{ systemMessage = $msg } | ConvertTo-Json -Compress
    } else {
        @{ decision = 'block'; reason = $msg } | ConvertTo-Json -Compress
    }
}
exit 0
