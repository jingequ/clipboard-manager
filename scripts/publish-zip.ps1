param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [string]$ZipPath
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishScript = Join-Path $PSScriptRoot "publish.ps1"

$defaultOutputDir = Join-Path $repoRoot "artifacts\publish\portable-single-file\$Runtime"
$resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) { $defaultOutputDir } else { $OutputDir }
$resolvedOutputDir = [System.IO.Path]::GetFullPath($resolvedOutputDir)

$defaultZipPath = Join-Path $repoRoot "artifacts\publish\ClipboardManager-$Runtime-$Configuration-portable-single-file.zip"
$resolvedZipPath = if ([string]::IsNullOrWhiteSpace($ZipPath)) { $defaultZipPath } else { $ZipPath }
$resolvedZipPath = [System.IO.Path]::GetFullPath($resolvedZipPath)

Write-Host "Publishing self-contained single-file portable build..." -ForegroundColor Cyan
& $publishScript -Runtime $Runtime -Configuration $Configuration -OutputDir $resolvedOutputDir -SingleFile

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$zipDirectory = Split-Path -Parent $resolvedZipPath
if (-not [string]::IsNullOrWhiteSpace($zipDirectory)) {
    New-Item -ItemType Directory -Force -Path $zipDirectory | Out-Null
}

if (Test-Path $resolvedZipPath) {
    Remove-Item $resolvedZipPath -Force
}

Compress-Archive -Path (Join-Path $resolvedOutputDir "*") -DestinationPath $resolvedZipPath

Write-Host "Zip package created." -ForegroundColor Green
Write-Host "Portable publish dir: $resolvedOutputDir" -ForegroundColor Green
Write-Host "Zip package: $resolvedZipPath" -ForegroundColor Green
