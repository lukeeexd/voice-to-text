# Builds a standalone Windows .exe into .\publish that needs no .NET install.
# Output: publish\VoiceToText.exe  +  publish\runtimes\  (keep them together).
$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' } # fall back to dotnet on PATH

$proj = Join-Path $PSScriptRoot 'src\VoiceToText\VoiceToText.csproj'
$out = Join-Path $PSScriptRoot 'publish'

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

& $dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out

Write-Host ""
Write-Host "Done. Standalone app:" -ForegroundColor Green
Write-Host "  $out\VoiceToText.exe"
Write-Host "Double-click it (keep the 'runtimes' folder beside it)."
