# Authenticode-sign a single PE (.exe/.dll) with the Calcpad code-signing cert.
#
# No-op unless $env:CALCPAD_SIGN_THUMBPRINT is set, so contributors and CI
# without a cert still get a working (unsigned) build. This is the desktop
# counterpart of signApphost() in
# vscode-calcpad/scripts/sync-bundled-server.mjs — same env vars, same signtool
# resolution, so the extension and desktop builds sign identically:
#
#   CALCPAD_SIGN_THUMBPRINT     SHA1 thumbprint of a code-signing cert in
#                               Cert:\CurrentUser\My (required to enable signing)
#   CALCPAD_SIGNTOOL            optional explicit path to signtool.exe
#   CALCPAD_SIGN_TIMESTAMP_URL  optional RFC-3161 timestamp URL
#
# Why signing matters here: a freshly-built, unsigned .exe has no reputation, so
# Windows Defender / SmartScreen / corporate EDR can block it (block-at-first-
# sight). Reputation is per-file-hash for unsigned binaries but per-publisher for
# signed ones, so signing with a trusted cert stops the block from re-firing on
# every rebuild. Only native PEs (.exe) get blocked; managed .dlls load through
# them.
#
# NOTE: signtool needs WRITE access to embed the signature. On a machine whose
# Defender is actively blocking the unsigned file, that write is itself denied
# ("Access is denied") until the build output is excluded from Defender (or the
# publisher cert is allowlisted by IT). Signing wiring is necessary but not
# sufficient on such machines — see the repo notes on Defender exclusions.

param(
    [Parameter(Mandatory = $true)][string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$thumbprint = $env:CALCPAD_SIGN_THUMBPRINT
if (-not $thumbprint) { return }   # signing disabled — no-op

if (-not (Test-Path $Path)) {
    Write-Host "[sign-file] target not found, skipping: $Path" -ForegroundColor Yellow
    return
}

function Resolve-SignTool {
    if ($env:CALCPAD_SIGNTOOL -and (Test-Path $env:CALCPAD_SIGNTOOL)) { return $env:CALCPAD_SIGNTOOL }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kitsBin = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $kitsBin) {
        $versioned = Get-ChildItem $kitsBin -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' -and $_.Directory.Parent.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Directory.Parent.Name } |
            Select-Object -Last 1
        if ($versioned) { return $versioned.FullName }
        $anyX64 = Get-ChildItem $kitsBin -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
        if ($anyX64) { return $anyX64.FullName }
    }
    return $null
}

$signtool = Resolve-SignTool
if (-not $signtool) {
    throw "CALCPAD_SIGN_THUMBPRINT is set but signtool.exe was not found. Install the Windows SDK or set CALCPAD_SIGNTOOL to its full path."
}

$ts = if ($env:CALCPAD_SIGN_TIMESTAMP_URL) { $env:CALCPAD_SIGN_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }

Write-Host "[sign-file] signing $Path with cert $thumbprint" -ForegroundColor Cyan
& $signtool sign /sha1 $thumbprint /fd SHA256 /tr $ts /td SHA256 $Path
if ($LASTEXITCODE -ne 0) {
    throw "signtool failed (exit $LASTEXITCODE) for $Path"
}

# Best-effort verify: a self-signed cert not chained to a trusted root fails
# /pa even though the signature was applied, so don't treat verify as fatal.
& $signtool verify /pa $Path 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[sign-file] verify failed (expected for a self-signed cert not yet trusted here); signature was still applied" -ForegroundColor Yellow
}
