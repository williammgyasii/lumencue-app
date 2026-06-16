<#
.SYNOPSIS
  Publishes a self-contained LumenCue build and compiles a Windows installer.

.DESCRIPTION
  1. Runs `dotnet publish` (self-contained, win-x64) into publish\app.
  2. Compiles installer\ChurchProjection.iss with Inno Setup (ISCC.exe).
  Output installer: publish\ChurchProjection-Setup-<version>.exe

.PARAMETER Version
  Version stamped on the build and installer (default 0.5.0).

.PARAMETER SkipPublish
  Reuse an existing publish\app folder and only (re)compile the installer.

.EXAMPLE
  installer\build-installer.ps1 -Version 0.5.0
#>
param(
    [string]$Version = "0.5.0",
    [string]$Runtime = "win-x64",
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Proj     = Join-Path $RepoRoot 'src\ChurchProjection.App\ChurchProjection.App.csproj'
$AppOut   = Join-Path $RepoRoot 'publish\app'
$Iss      = Join-Path $PSScriptRoot 'ChurchProjection.iss'

if (-not $SkipPublish) {
    Write-Host "==> Publishing self-contained $Runtime build (v$Version)..." -ForegroundColor Cyan
    if (Test-Path $AppOut) { Remove-Item $AppOut -Recurse -Force }
    dotnet publish $Proj -c Release -r $Runtime --self-contained true `
        -p:PublishReadyToRun=true -p:Version=$Version -o $AppOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

# Confirm embedded API keys made it into the build.
$localCfg = Join-Path $AppOut 'appsettings.local.json'
if (Test-Path $localCfg) {
    Write-Host "API keys embedded (appsettings.local.json present in build)." -ForegroundColor Green
} else {
    Write-Warning "appsettings.local.json NOT in build - app will run with free Bible API + offline STT only."
}

# Locate the Inno Setup compiler.
$iscc = $null
foreach ($p in @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $p) { $iscc = $p; break }
}
if (-not $iscc) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found."
    Write-Host   "Install it, then re-run this script:" -ForegroundColor Yellow
    Write-Host   "    winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host   "Self-contained build is ready at: $AppOut" -ForegroundColor Yellow
    return
}

Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host "==> Installer created: $RepoRoot\publish\ChurchProjection-Setup-$Version.exe" -ForegroundColor Green
