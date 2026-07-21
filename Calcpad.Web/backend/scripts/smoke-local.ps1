<#
.SYNOPSIS
    Builds and runs the calc-only Calcpad.Server container and smoke-tests it.

.DESCRIPTION
    Starts docker-compose.smoke.yml (single container, no auth/storage/Garage),
    waits for it to become healthy, then exercises the /health and
    /api/calcpad/convert endpoints. Mirrors the shape of a future Cloud Run
    deployment: one process, port 8080, stateless.

.PARAMETER Rebuild
    Force a clean image rebuild (docker compose build --no-cache).

.PARAMETER KeepRunning
    Leave the container running after the smoke test passes. By default the
    container is stopped and removed on success.
#>
param(
    [switch]$Rebuild,
    [switch]$KeepRunning
)

# Note: intentionally NOT $ErrorActionPreference = "Stop" — the Docker CLI
# writes normal progress lines to stderr, which some PowerShell versions
# turn into terminating errors under "Stop". We check $LASTEXITCODE
# explicitly after each command that matters instead.
$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$composeFile = Join-Path $scriptDir "..\docker-compose.smoke.yml"
$baseUrl = "http://localhost:8081"

function Write-Step($message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Fail($message) {
    Write-Host "FAILED: $message" -ForegroundColor Red
    Write-Host ""
    Write-Host "Container logs:" -ForegroundColor Yellow
    docker compose -f $composeFile logs
    exit 1
}

Push-Location $scriptDir
try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Fail "Docker is not installed or not on PATH."
    }

    Write-Step "Stopping any existing smoke container"
    docker compose -f $composeFile down --remove-orphans | Out-Null

    if ($Rebuild) {
        Write-Step "Rebuilding image (no cache)"
        docker compose -f $composeFile build --no-cache
    } else {
        Write-Step "Building image"
        docker compose -f $composeFile build
    }
    if ($LASTEXITCODE -ne 0) { Fail "docker compose build failed." }

    Write-Step "Starting container"
    docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up failed to start the container." }

    Write-Step "Waiting for $baseUrl/health"
    $ready = $false
    for ($i = 1; $i -le 30; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 3
            if ($resp.StatusCode -eq 200) {
                $ready = $true
                break
            }
        } catch {
            # Not up yet; keep polling.
        }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { Fail "Server did not become healthy within 60 seconds." }
    Write-Host "Server is up." -ForegroundColor Green

    Write-Step "Checking GET /api/calcpad/sample"
    try {
        $sample = Invoke-WebRequest -Uri "$baseUrl/api/calcpad/sample" -UseBasicParsing -TimeoutSec 5
        if ($sample.StatusCode -ne 200) { Fail "/api/calcpad/sample returned status $($sample.StatusCode)." }
    } catch {
        Fail "/api/calcpad/sample request failed: $($_.Exception.Message)"
    }
    Write-Host "Sample endpoint OK." -ForegroundColor Green

    Write-Step "Checking POST /api/calcpad/convert"
    $body = @{ content = "x = 5`ny = x + 3" } | ConvertTo-Json
    try {
        $convert = Invoke-WebRequest -Uri "$baseUrl/api/calcpad/convert" -Method Post `
            -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 10
        if ($convert.StatusCode -ne 200) { Fail "/api/calcpad/convert returned status $($convert.StatusCode)." }
        if ($convert.Content.Length -eq 0) { Fail "/api/calcpad/convert returned an empty response." }
    } catch {
        Fail "/api/calcpad/convert request failed: $($_.Exception.Message)"
    }
    Write-Host "Convert endpoint OK." -ForegroundColor Green

    Write-Host ""
    Write-Host "Smoke test passed." -ForegroundColor Green
    Write-Host "  Sample:  $baseUrl/api/calcpad/sample"
    Write-Host "  Convert: $baseUrl/api/calcpad/convert"
    Write-Host "  Health:  $baseUrl/health"

    if ($KeepRunning) {
        Write-Host ""
        Write-Host "Container left running. Stop it with:" -ForegroundColor Yellow
        Write-Host "  docker compose -f `"$composeFile`" down"
    } else {
        Write-Step "Stopping container"
        docker compose -f $composeFile down
    }
} finally {
    Pop-Location
}
