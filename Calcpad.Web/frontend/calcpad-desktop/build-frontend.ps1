# Rebuild the calcpad-web frontend (and its calcpad-frontend dependency) into
# calcpad-web/dist, which Tauri embeds into the app binary at compile time.
#
# Invoked from tauri.windows.conf.json's beforeBuildCommand / beforeDevCommand.
# Kept as a standalone -File script on purpose: Tauri wraps beforeBuildCommand
# in `cmd /S /C`, and an inline `powershell -Command "& { ... }"` gets mangled
# there — cmd treats the `&` as a command separator, so the frontend never
# rebuilt and the app shipped a stale dist.

param(
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalNpm  = Join-Path $ScriptDir 'local-npm.ps1'

Push-Location $ScriptDir
try {
    Remove-Item -Recurse -Force '..\calcpad-web\dist' -ErrorAction SilentlyContinue

    & $LocalNpm --prefix ../calcpad-frontend run build
    if ($LASTEXITCODE -ne 0) { throw "calcpad-frontend build failed ($LASTEXITCODE)" }

    if ($Watch) {
        Start-Process -NoNewWindow -FilePath powershell -WorkingDirectory $ScriptDir `
            -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $LocalNpm, '--prefix', '../calcpad-frontend', 'run', 'watch'
        & $LocalNpm --prefix ../calcpad-web run build:watch
    }
    else {
        & $LocalNpm --prefix ../calcpad-web run build
        if ($LASTEXITCODE -ne 0) { throw "calcpad-web build failed ($LASTEXITCODE)" }
    }
}
finally {
    Pop-Location
}
