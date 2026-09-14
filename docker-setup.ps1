<#
.SYNOPSIS
    Builds and starts the Smart-X Docker stack, then opens the dashboard in your browser.

.DESCRIPTION
    Runs `docker compose up --build`, waits for the client container to actually
    start responding (rather than guessing with a fixed sleep), then launches the
    default browser at the dashboard's address. Safe to re-run; Docker will reuse
    cached layers for anything that hasn't changed.
#>

$ErrorActionPreference = "Stop"

Write-Host "Checking Docker is running..." -ForegroundColor Cyan
docker info > $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker doesn't seem to be running. Start Docker Desktop and try again." -ForegroundColor Red
    exit 1
}

Write-Host "Building and starting the Smart-X containers (this can take a few minutes on the first run)..." -ForegroundColor Cyan
docker compose up --build -d

if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed to start the stack. See the output above for details." -ForegroundColor Red
    exit $LASTEXITCODE
}

$clientUrl = "http://localhost:5128"
Write-Host "Waiting for the client to become reachable at $clientUrl..." -ForegroundColor Cyan

$maxAttempts = 30
$ready = $false
for ($i = 0; $i -lt $maxAttempts; $i++) {
    try {
        $response = Invoke-WebRequest -Uri $clientUrl -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
            $ready = $true
            break
        }
    } catch {
        # Not up yet -- keep polling.
    }
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    Write-Host "The client didn't respond after $($maxAttempts * 2) seconds. Check 'docker compose logs' for details." -ForegroundColor Yellow
    Write-Host "Opening the browser anyway in case it's just slow to finish starting..." -ForegroundColor Yellow
}

# Append a cache-busting query string for the actual browser launch (but not
# for the polling checks above). Browsers key their disk cache on the exact
# URL, and if this address was ever visited while the client returned the
# wrong Content-Type (e.g. before a nginx.conf fix), the browser will keep
# replaying that cached "download" response for the bare URL forever. A
# unique query string every run guarantees a fresh request instead.
$cacheBustedUrl = "$clientUrl/?nocache=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
Write-Host "Opening $cacheBustedUrl in your default browser..." -ForegroundColor Green
Start-Process $cacheBustedUrl
