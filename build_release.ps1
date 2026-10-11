# ScreenPi Release Build Script
# Packages TrackerService and DashboardUI into standalone, self-contained Windows executables

$ErrorActionPreference = "Stop"

# Ensure local dotnet is in PATH if present
if (Test-Path "$env:LOCALAPPDATA\dotnet") {
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\dotnet"
    $env:PATH = "$env:LOCALAPPDATA\dotnet;$env:PATH"
}

$PublishDir = ".\publish\ScreenPi"
$ZipFile = ".\ScreenPi-Windows-x64.zip"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " ScreenPi Release Packaging (win-x64)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Clean existing publish directory and zip
if (Test-Path $PublishDir) {
    Write-Host "[Clean] Removing existing publish folder..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $PublishDir
}
if (Test-Path $ZipFile) {
    Write-Host "[Clean] Removing existing release archive..." -ForegroundColor Gray
    Remove-Item -Force $ZipFile
}

# 1. Publish TrackerService (Headless Background Monitor)
Write-Host "`n[1/3] Publishing TrackerService (Self-contained, Single-file, Trimmed)..." -ForegroundColor Yellow
dotnet publish TrackerService\TrackerService.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o .\publish\ScreenPi

if ($LASTEXITCODE -ne 0) {
    Write-Error "TrackerService publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 2. Publish DashboardUI (WinUI 3 Fluent App)
Write-Host "`n[2/3] Publishing DashboardUI (Self-contained, WindowsAppSDK Unpackaged)..." -ForegroundColor Yellow
dotnet publish DashboardUI\DashboardUI.csproj -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=true -o .\publish\ScreenPi

if ($LASTEXITCODE -ne 0) {
    Write-Error "DashboardUI publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Create Distribution Archive
Write-Host "`n[3/3] Compressing package into $ZipFile..." -ForegroundColor Yellow
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFile -Force

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " SUCCESS: Release package ready!" -ForegroundColor Green
Write-Host " Archive: $(Resolve-Path $ZipFile)" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
