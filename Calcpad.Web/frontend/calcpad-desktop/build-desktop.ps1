# Build script for CalcPad Desktop on Windows (PowerShell)
# Mirrors build-desktop.sh: builds the self-contained .NET server for win-x64
# then prints next steps (run `npx neu build` to package).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Info  { param($msg) Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Ok    { param($msg) Write-Host "[OK]    $msg" -ForegroundColor Green }
function Warn  { param($msg) Write-Host "[WARN]  $msg" -ForegroundColor Yellow }
function Err   { param($msg) Write-Host "[ERR]   $msg" -ForegroundColor Red; exit 1 }

$ScriptDir     = $PSScriptRoot
$FrontendDir   = Split-Path $ScriptDir -Parent
$SyncScript    = Join-Path $FrontendDir "vscode-calcpad\scripts\sync-bundled-server.mjs"
$ExtensionsDir = Join-Path $ScriptDir "extensions\server"

Write-Host ""
Write-Host "+----------------------------------------------+" -ForegroundColor Cyan
Write-Host "|   CalcPad Desktop Build Script (Windows)     |" -ForegroundColor Cyan
Write-Host "+----------------------------------------------+" -ForegroundColor Cyan
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Err "dotnet CLI not found. Install .NET 10 SDK." }
if (-not (Get-Command node   -ErrorAction SilentlyContinue)) { Err "node not found. Install Node.js." }
if (-not (Test-Path $SyncScript)) { Err "Sync script not found: $SyncScript" }

Info "Using dotnet $(dotnet --version 2>$null)"
Info "Target platform: win-x64"

# Clean previous server build
if (Test-Path $ExtensionsDir) {
    Info "Cleaning previous server build..."
    Get-ChildItem -Path $ExtensionsDir -Exclude ".gitkeep" -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Path $ExtensionsDir -Force | Out-Null

# Build .NET server (self-contained, win-x64)
Info "Building Calcpad.Server for win-x64..."
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'
node $SyncScript `
    "--target=$ExtensionsDir" `
    --rid=win-x64 `
    --configuration=Release `
    --keep-skia-natives

$ExePath = Join-Path $ExtensionsDir "Calcpad.Server.exe"
if (-not (Test-Path $ExePath)) { Err "Build failed - Calcpad.Server.exe not found in $ExtensionsDir" }

$SizeMb = [math]::Round(
    (Get-ChildItem $ExtensionsDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Ok "Server built: $ExePath ($SizeMb MB total)"

Write-Host ""
Write-Host "===============================================" -ForegroundColor Green
Write-Host "  CalcPad Desktop server build complete       " -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Cyan
Write-Host "    Dev mode:  npx neu run   (from calcpad-desktop/)" -ForegroundColor White
Write-Host "    Package:   npx neu build (from calcpad-desktop/)" -ForegroundColor White
Write-Host ""
