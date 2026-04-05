param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$issPath = Join-Path $root "installer\\ClipboardManager.iss"

Write-Host "Publishing app first..." -ForegroundColor Cyan
powershell -ExecutionPolicy Bypass -File $publishScript -Runtime $Runtime

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    Write-Warning "Inno Setup compiler (iscc) was not found in PATH. App publish is ready, but installer was not created."
    Write-Host "Install Inno Setup and run this script again to generate the installer." -ForegroundColor Yellow
    exit 0
}

Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc.Source $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Write-Host "Installer created under the dist folder." -ForegroundColor Green
