# Creates a portable Windows ZIP of the CalcPad Desktop app.
# Output: packaging/windows/CalcpadCEWeb-portable[-<version>].zip
#
# Prerequisites: run "Desktop: Build Server + Assets" and "Desktop: Package" first.

param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Info { param($msg) Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Ok   { param($msg) Write-Host "[OK]    $msg" -ForegroundColor Green }
function Err  { param($msg) Write-Host "[ERR]   $msg" -ForegroundColor Red; exit 1 }

$DistDir  = Resolve-Path (Join-Path $PSScriptRoot "..\..\dist\calcpad-desktop")
$OutDir   = $PSScriptRoot
$ZipName  = if ($Version) { "CalcpadCEWeb-portable-$Version.zip" } else { "CalcpadCEWeb-portable.zip" }
$ZipPath  = Join-Path $OutDir $ZipName

# Validate build outputs exist
$NeuExe    = Join-Path $DistDir "calcpad-desktop-win_x64.exe"
$Resources = Join-Path $DistDir "resources.neu"
$ServerExe = Join-Path $DistDir "extensions\server\Calcpad.Server.exe"

if (-not (Test-Path $NeuExe))    { Err "calcpad-desktop-win_x64.exe not found. Run 'Desktop: Package' first." }
if (-not (Test-Path $Resources)) { Err "resources.neu not found. Run 'Desktop: Package' first." }
if (-not (Test-Path $ServerExe)) { Err "Calcpad.Server.exe not found. Run 'Desktop: Build Server + Assets' (Windows) first." }

# Sign the Neutralino app exe (no-op unless CALCPAD_SIGN_THUMBPRINT is set).
# Signing the dist exe in place means both the directly-run dist binary and the
# copy staged into the zip below carry the signature. The embedded server
# apphost (extensions\server\Calcpad.Server.exe) was already signed by
# build-desktop.ps1 via sync-bundled-server.mjs.
$SignFile = Join-Path $PSScriptRoot "sign-file.ps1"
& $SignFile -Path $NeuExe

Info "Staging portable build..."

$StageDir = Join-Path $env:TEMP "calcpad-portable-$(Get-Random)"
New-Item -ItemType Directory -Path $StageDir | Out-Null

try {
    # Main executable — rename to friendly app name
    Copy-Item $NeuExe (Join-Path $StageDir "CalcpadCEWeb.exe")

    # Neutralino resource bundle
    Copy-Item $Resources $StageDir

    # Server extension (all files)
    Copy-Item (Join-Path $DistDir "extensions") $StageDir -Recurse

    # Create ZIP
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path (Join-Path $StageDir "*") -DestinationPath $ZipPath

    $SizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
    Ok "Created: $ZipPath ($SizeMb MB)"
} finally {
    Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue
}
