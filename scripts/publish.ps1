param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [switch]$FrameworkDependent,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\ClipboardManager.App\ClipboardManager.App.csproj"
$project = [System.IO.Path]::GetFullPath($project)

$defaultOutputDir = Join-Path $PSScriptRoot "..\artifacts\publish\portable\$Runtime"
$resolvedOutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) { $defaultOutputDir } else { $OutputDir }
$resolvedOutputDir = [System.IO.Path]::GetFullPath($resolvedOutputDir)

$selfContainedValue = if ($FrameworkDependent) { "false" } else { "true" }
$singleFileValue = if ($SingleFile) { "true" } else { "false" }

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null

try {
    Get-ChildItem $resolvedOutputDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}
catch {
    throw "Failed to clean publish output '$resolvedOutputDir'. Close any running app copied from this directory, then try again. $($_.Exception.Message)"
}

Write-Host "Publishing Clipboard Manager portable build..." -ForegroundColor Cyan
Write-Host "Project: $project"
Write-Host "Runtime: $Runtime"
Write-Host "Configuration: $Configuration"
Write-Host "SelfContained: $selfContainedValue"
Write-Host "SingleFile: $singleFileValue"
Write-Host "Output: $resolvedOutputDir"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=$singleFileValue `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $resolvedOutputDir

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

Write-Host "Publish completed." -ForegroundColor Green
Write-Host "Portable output: $resolvedOutputDir" -ForegroundColor Green
