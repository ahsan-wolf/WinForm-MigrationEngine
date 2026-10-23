# Starts the Migrator UI: builds the React UI once (npm install + build in web/), then runs the host and opens the browser.
# Usage: pwsh tools/migrator-ui/start.ps1 [-Port 5280] [-NoBrowser]
param([int]$Port = 5280, [switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$web = Join-Path $root 'web'
if (-not (Test-Path (Join-Path $web 'dist/index.html'))) {
    Push-Location $web
    try { npm install; npm run build } finally { Pop-Location }
}
$url = "http://127.0.0.1:$Port"
if (-not $NoBrowser) { Start-Job { Start-Sleep 6; Start-Process $using:url } | Out-Null }
dotnet run --project (Join-Path $root 'host') -- --urls $url
