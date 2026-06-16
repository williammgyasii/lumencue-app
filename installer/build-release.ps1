<#
.SYNOPSIS
  Publishes LumenCue (self-contained win-x64), packs a Velopack release, and
  (optionally) uploads it to the private GitHub Releases for OTA updates.

.DESCRIPTION
  1. dotnet publish -> publish\app  (self-contained, win-x64)
  2. vpk pack        -> publish\releases  (Setup.exe + full/delta nupkg + RELEASES)
  3. vpk upload github (unless -SkipUpload)  -> creates/updates the GitHub release

  End users on prior versions then get the update in-app via Velopack's GithubSource.

.PARAMETER Version
  SemVer stamped on the build and the Velopack package (e.g. 0.6.5). Required.

.PARAMETER Token
  GitHub token with write access to the releases repo. Defaults to the token from
  the GitHub CLI (`gh auth token`), so just stay logged in as the repo owner.

.PARAMETER SkipUpload
  Build + pack only; do not publish to GitHub. Useful for testing the installer locally.

.PARAMETER SkipPublish
  Reuse an existing publish\app folder and only (re)pack/upload.

.EXAMPLE
  installer\build-release.ps1 -Version 0.6.5
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Token,
    [string]$RepoUrl = "https://github.com/williammgyasii/lumencue-app",
    [string]$Runtime = "win-x64",
    [switch]$SkipUpload,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Proj       = Join-Path $RepoRoot 'src\ChurchProjection.App\ChurchProjection.App.csproj'
$AppOut     = Join-Path $RepoRoot 'publish\app'
$ReleaseDir = Join-Path $RepoRoot 'publish\releases'
$Icon       = Join-Path $RepoRoot 'src\ChurchProjection.App\Assets\app.ico'

$PackId   = 'LumenCue'
$MainExe  = 'ChurchProjection.App.exe'

# --- 1. Publish self-contained win-x64 ------------------------------------------------
if (-not $SkipPublish) {
    Write-Host "==> Publishing self-contained $Runtime build (v$Version)..." -ForegroundColor Cyan
    if (Test-Path $AppOut) { Remove-Item $AppOut -Recurse -Force }
    dotnet publish $Proj -c Release -r $Runtime --self-contained true `
        -p:Version=$Version -o $AppOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

# Confirm embedded API keys + update token made it into the build.
$localCfg = Join-Path $AppOut 'appsettings.local.json'
if (Test-Path $localCfg) {
    Write-Host "appsettings.local.json present in build (API keys + update token embedded)." -ForegroundColor Green
} else {
    Write-Warning "appsettings.local.json NOT in build - app will run with free APIs and CANNOT self-update (no token)."
}

# --- 2. Ensure the Velopack CLI (vpk) is installed ------------------------------------
$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    Write-Host "==> Installing Velopack CLI (vpk) as a global dotnet tool..." -ForegroundColor Cyan
    dotnet tool install --global vpk
    if ($LASTEXITCODE -ne 0) { throw "Failed to install vpk. Install manually: dotnet tool install --global vpk" }
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

# --- 3. Pack the Velopack release -----------------------------------------------------
Write-Host "==> Packing Velopack release v$Version..." -ForegroundColor Cyan
$packArgs = @(
    'pack',
    '--packId',      $PackId,
    '--packVersion', $Version,
    '--packDir',     $AppOut,
    '--mainExe',     $MainExe,
    '--packTitle',   'LumenCue',
    '--packAuthors', 'LumenCue',
    '--outputDir',   $ReleaseDir
)
if (Test-Path $Icon) { $packArgs += @('--icon', $Icon) }
vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "==> Release artifacts ready in: $ReleaseDir" -ForegroundColor Green

# --- 4. Upload to GitHub Releases -----------------------------------------------------
if ($SkipUpload) {
    Write-Host "Skipping upload (-SkipUpload). Installer: $ReleaseDir\$PackId-win-Setup.exe" -ForegroundColor Yellow
    return
}

if (-not $Token) {
    Write-Host "==> No -Token supplied; using GitHub CLI token (gh auth token)..." -ForegroundColor Cyan
    $Token = (gh auth token).Trim()
    if (-not $Token) { throw "No token available. Pass -Token or run: gh auth login" }
}

Write-Host "==> Uploading release v$Version to $RepoUrl..." -ForegroundColor Cyan
vpk upload github `
    --repoUrl     $RepoUrl `
    --token       $Token `
    --outputDir   $ReleaseDir `
    --releaseName "LumenCue $Version" `
    --tag         "v$Version" `
    --publish
if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed" }

Write-Host "==> Released v$Version. Existing installs will see the update on next launch." -ForegroundColor Green
