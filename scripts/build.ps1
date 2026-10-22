<#
.SYNOPSIS
  Builds, tests and packages GW2 Legendary Tracker the same way the release workflow does.

.DESCRIPTION
  Restores in locked mode, builds in Release, runs the tests, publishes a self-contained win-x64 build, zips it,
  optionally compiles the installer (needs Inno Setup 6) and writes SHA256 checksums. Everything lands in -Output.

.EXAMPLE
  ./scripts/build.ps1                 # build, test, publish, zip
  ./scripts/build.ps1 -Installer      # ...and compile the installer
  ./scripts/build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$Installer,
    [string]$Output = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Step {
    param([string]$Title, [scriptblock]$Command)
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Stap mislukt: $Title (exitcode $LASTEXITCODE)" }
}

$version = (dotnet msbuild src/Gw2LegendaryTracker.App -getProperty:Version).Trim()
if (-not $version) { throw 'De versie kon niet worden bepaald uit src/Gw2LegendaryTracker.App.' }
Write-Host "Versie $version" -ForegroundColor Green

$publishDir = Join-Path $Output 'win-x64'
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Path $Output | Out-Null

# ContinuousIntegrationBuild makes the output independent of the folder it was built in.
$ci = '-p:ContinuousIntegrationBuild=true'

Invoke-Step 'Restore (vergrendeld)' { dotnet restore --locked-mode }
Invoke-Step 'Build' { dotnet build --no-restore -c Release $ci }
if (-not $SkipTests) { Invoke-Step 'Tests' { dotnet test --no-build -c Release $ci } }
Invoke-Step 'Publish (self-contained, win-x64)' {
    dotnet publish src/Gw2LegendaryTracker.App -c Release -r win-x64 --self-contained -p:RestoreLockedMode=true $ci -o $publishDir
}

$zip = Join-Path $Output "Gw2LegendaryTracker-$version-win-x64.zip"
Write-Host "==> Zip: $zip" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal

if ($Installer) {
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 niet gevonden. Installeer het (https://jrsoftware.org/isdl.php) of laat -Installer weg.' }

    Invoke-Step 'Installer (Inno Setup)' { & $iscc "/DAppVersion=$version" "/O$Output" installer/Gw2LegendaryTracker.iss }
}

Write-Host '==> Checksums (SHA256)' -ForegroundColor Cyan
$sums = Get-ChildItem $Output -File | Where-Object { $_.Extension -in '.zip', '.exe' } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content -Path (Join-Path $Output 'SHA256SUMS.txt') -Encoding ascii
$sums | ForEach-Object { Write-Host $_ }

Write-Host "Klaar. Bestanden staan in '$Output'." -ForegroundColor Green
