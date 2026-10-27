<#
.SYNOPSIS
  Publishes Curfew and wraps it in an installer: artifacts\Curfew-Setup-<version>.exe

.EXAMPLE
  .\tools\build-installer.ps1 -Version 1.0.0

  Needs Inno Setup 6 (winget install JRSoftware.InnoSetup). The app is published self-contained, so the person who
  installs it does not need .NET.
#>
param(
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Curfew.App') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" (Join-Path $root 'installer\Curfew.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

Get-Item (Join-Path $root "artifacts\Curfew-Setup-$Version.exe") | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
