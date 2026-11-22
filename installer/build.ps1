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

# Test de démarrage : la version publiée (optimisée) doit tenir 5 s sans planter, sur des données jetables.
$probe = Join-Path $env:TEMP "campus-probe-$Version"
if (Test-Path $probe) { Remove-Item $probe -Recurse -Force }
$env:CAMPUS_DATA_DIR = $probe; $env:CAMPUS_ROOT = Join-Path $probe 'lib'
$p = Start-Process (Join-Path $out 'Campus Manager.exe') -ArgumentList '--hidden' -PassThru
Start-Sleep 5
$alive = -not $p.HasExited
if ($alive) { Stop-Process -Id $p.Id -Force; Start-Sleep 1 }
Remove-Item Env:CAMPUS_DATA_DIR, Env:CAMPUS_ROOT
Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
if (-not $alive) { throw "La version publiée plante au démarrage (code $($p.ExitCode)) : installateur non généré." }

& $iscc /Qp "/DAppVersion=$Version" installer\CampusManager.iss
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué." }

$setup = Join-Path $root "installer\Output\Campus Manager Setup $Version.exe"
Write-Output ("Installateur : {0} ({1:N0} Mo)" -f $setup, ((Get-Item $setup).Length / 1MB))
