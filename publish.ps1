param([switch]$SelfContained)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Stop-Process -Name "RoleplayOverlay" -Force -ErrorAction SilentlyContinue
$out = (Resolve-Path "$PSScriptRoot\..").Path
$sc  = if ($SelfContained) { "true" } else { "false" }
dotnet publish -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:SatelliteResourceLanguages=fr `
    -o $out
Write-Host ""
Write-Host "OK -> $out\RoleplayOverlay.exe" -ForegroundColor Green