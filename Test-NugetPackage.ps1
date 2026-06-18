param(
    [string]$Version = "9.9.999-omega",
    [switch]$KeepPackages
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
# Propagate non-zero dotnet exit codes as terminating errors
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Split-Path -Parent $PSCommandPath
$projectPath = Join-Path $repoRoot "MiniBus.NugetIntegrationTests"
$configPath = Join-Path $projectPath "nuget.integration-tests.config"
$artifactsDir = Join-Path $projectPath "test/artifacts"
$packagesDir = Join-Path $projectPath "test/packages"

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Missing config file: $configPath"
}

Write-Host "==> Cleaning previous local test artifacts"
if (Test-Path -LiteralPath $artifactsDir) {
    Remove-Item -LiteralPath $artifactsDir -Recurse -Force
}
if (Test-Path -LiteralPath $packagesDir) {
    Remove-Item -LiteralPath $packagesDir -Recurse -Force
}

Write-Host "==> Packing MiniBus version $Version"
dotnet pack (Join-Path $repoRoot "MiniBus") -c Release -o $artifactsDir -p:MinVerVersionOverride=$Version

Write-Host "==> Restoring NuGet integration tests"
dotnet restore $projectPath --packages $packagesDir --configfile $configPath

Write-Host "==> Building NuGet integration tests"
dotnet build $projectPath -c Release --packages $packagesDir --no-restore

Write-Host "==> Running NuGet integration tests"
dotnet test $projectPath -c Release --no-build --no-restore

if (-not $KeepPackages) {
    Write-Host "==> Cleaning local test artifacts and restore folder"
    if (Test-Path -LiteralPath $artifactsDir) {
        Remove-Item -LiteralPath $artifactsDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $packagesDir) {
        Remove-Item -LiteralPath $packagesDir -Recurse -Force
    }
}

Write-Host "==> NuGet integration test flow completed successfully"
