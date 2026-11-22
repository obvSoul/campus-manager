# Construit la version optimisée (Windows x64) et l'installateur.
# Usage : installer\build.ps1 -Version 0.2.0 [-SkipTests]
# Résultat : installer\Output\Campus Manager Setup <version>.exe
param([string]$Version = "0.2.0", [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 est introuvable (winget install JRSoftware.InnoSetup).' }

if (-not $SkipTests) {
    dotnet test CampusManager.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Des tests ont échoué : installateur non généré.' }
}

$out = "publish\$Version"  # un dossier par version : une copie en cours d'exécution n'est jamais touchée
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src\CampusManager.App -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:Version=$Version -p:DebugType=none -p:ContinuousIntegrationBuild=true -o $out -v q
if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }

& $iscc /Qp "/DAppVersion=$Version" installer\CampusManager.iss
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué." }

$setup = Join-Path $root "installer\Output\Campus Manager Setup $Version.exe"
Write-Output ("Installateur : {0} ({1:N0} Mo)" -f $setup, ((Get-Item $setup).Length / 1MB))
