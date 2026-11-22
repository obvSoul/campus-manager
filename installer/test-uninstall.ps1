# Teste l'installation puis la désinstallation SANS toucher à l'installation réelle :
# variante « Campus Manager Test » (autre AppId, autre verrou, autre dossier, autres données, pas de lancement).
# Usage : installer\test-uninstall.ps1 [-Version 0.2.0]   (après installer\build.ps1)
param([string]$Version = "0.2.0")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
$work = Join-Path $env:TEMP "campus-uninstall-test"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null

# 1. Variante de test du script d'installation.
$iss = [IO.File]::ReadAllText("$PSScriptRoot\CampusManager.iss", [Text.Encoding]::UTF8)
$iss = $iss.Replace('AppId={{3B8F6C21-9D4E-4A7B-8E25-6C1F0D9A7B43}', 'AppId={{9C1E0D44-7A2B-4F63-B5D8-1E2F3A4B5C6D}')
$iss = $iss.Replace('AppName=Campus Manager', 'AppName=Campus Manager Test').Replace('AppVerName=Campus Manager ', 'AppVerName=Campus Manager Test ')
$iss = $iss.Replace('DefaultDirName={userpf}\Campus Manager', 'DefaultDirName={userpf}\Campus Manager Test').Replace('DefaultGroupName=Campus Manager', 'DefaultGroupName=Campus Manager Test')
$iss = $iss.Replace('UninstallDisplayName=Campus Manager', 'UninstallDisplayName=Campus Manager Test')
$iss = $iss.Replace('AppMutex=Local\CampusManager.Instance', 'AppMutex=Local\CampusManager.UninstallTest')
$iss = $iss.Replace('OutputDir=Output', "OutputDir=$work").Replace('OutputBaseFilename=Campus Manager Setup {#AppVersion}', 'OutputBaseFilename=test-setup')
$iss = $iss.Replace('Source: "..\publish\', "Source: `"$root\publish\").Replace('SetupIconFile=..\build\', "SetupIconFile=$root\build\")
$iss = $iss.Replace('Name: "{autoprograms}\Campus Manager"', 'Name: "{autoprograms}\Campus Manager Test"').Replace('Name: "{autodesktop}\Campus Manager"', 'Name: "{autodesktop}\Campus Manager Test"')
$iss = $iss.Replace('ValueName: "Campus Manager"', 'ValueName: "Campus Manager Test"')
$iss = $iss.Replace("{userappdata}\Campus Manager'", "{userappdata}\Campus Manager Test'")
$iss = [Text.RegularExpressions.Regex]::Replace($iss, '(?ms)^\[Run\].*?(?=^\[)', '')   # ne lance pas l'appli
if ($iss.Contains("{userappdata}\Campus Manager'")) { throw 'Variante incomplète : elle viserait les vraies données.' }
[IO.File]::WriteAllText("$work\test.iss", $iss, (New-Object Text.UTF8Encoding $true))
& $iscc /Qp "/DAppVersion=$Version" "$work\test.iss"; if ($LASTEXITCODE) { throw 'Compilation de la variante impossible.' }

# Exécuté comme Windows lui-même (le registre vu depuis certaines applis peut être séparé).
function Run-AsWindows([string]$cmd) {
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd }
    while (Get-CimInstance Win32_Process -Filter "ProcessId=$($r.ProcessId)") { Start-Sleep -Milliseconds 300 }
}
# Présence d'un fichier vue par Windows (une session lancée depuis une appli MSIX peut voir une copie à part de AppData).
function Exists-AsWindows([string]$path) {
    $out = "C:\Users\Public\cm-existtest.txt"
    Run-AsWindows "cmd /c if exist `"$path`" (echo oui> `"$out`") else (echo non> `"$out`")"
    $t = (Get-Content $out -Raw).Trim(); Remove-Item $out
    return $t -eq 'oui'
}
function Reg-Has([string]$key, [string]$value) {
    $out = "C:\Users\Public\cm-regtest.txt"
    $q = if ($value) { "reg query `"$key`" /v `"$value`"" } else { "reg query `"$key`"" }
    Run-AsWindows "cmd /c $q > `"$out`" 2>&1"
    $t = Get-Content $out -Raw; Remove-Item $out
    return $t -notmatch 'ERREUR|ERROR|introuvable|unable to find'
}

$dir = Join-Path $env:LOCALAPPDATA "Programs\Campus Manager Test"
$data = Join-Path $env:APPDATA "Campus Manager Test"
$menu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Campus Manager Test.lnk"
$desk = Join-Path ([Environment]::GetFolderPath('Desktop')) "Campus Manager Test.lnk"
$uninstKey = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9C1E0D44-7A2B-4F63-B5D8-1E2F3A4B5C6D}_is1'
$runKey = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run'
$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Campus Manager'
$docsBefore = (Get-ChildItem $docs -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
$results = New-Object Collections.Generic.List[string]
function Check([string]$label, [bool]$ok) { $s = if ($ok) { "OK   " } else { "ÉCHEC" }; $results.Add("$s $label") }

function Install {
    Run-AsWindows "`"$work\test-setup.exe`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=desktopicon"
    # Ce que l'appli crée elle-même à l'usage : données et démarrage avec Windows.
    Run-AsWindows "cmd /c mkdir `"$data`" & echo base de test> `"$data\library.db`""
    Run-AsWindows "reg add `"$runKey`" /v `"Campus Manager Test`" /t REG_SZ /d `"\`"$dir\Campus Manager.exe\`" --hidden`" /f"
}

# 2. Installation
Install
Check 'installé : programme' (Test-Path "$dir\Campus Manager.exe")
Check 'installé : raccourci menu Démarrer' (Test-Path $menu)
Check 'installé : raccourci Bureau' (Test-Path $desk)
Check 'installé : entrée « Applications »' (Reg-Has $uninstKey $null)
Check 'installé : démarrage avec Windows présent' (Reg-Has $runKey 'Campus Manager Test')

# 3. Désinstallation en gardant les données (réponse par défaut)
Run-AsWindows "`"$dir\unins000.exe`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
Start-Sleep 2
Check 'désinstallé : programme retiré' (-not (Test-Path "$dir\Campus Manager.exe"))
Check 'désinstallé : raccourci menu Démarrer retiré' (-not (Test-Path $menu))
Check 'désinstallé : raccourci Bureau retiré' (-not (Test-Path $desk))
Check 'désinstallé : entrée « Applications » retirée' (-not (Reg-Has $uninstKey $null))
Check 'désinstallé : démarrage avec Windows retiré' (-not (Reg-Has $runKey 'Campus Manager Test'))
Check 'désinstallé : base GARDÉE par défaut' (Exists-AsWindows "$data\library.db")

# 4. Désinstallation avec suppression des données
Install
Run-AsWindows "`"$dir\unins000.exe`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DELETEDATA=1"
Start-Sleep 2
Check 'avec /DELETEDATA=1 : base supprimée' (-not (Exists-AsWindows $data))

# 5. Les cours ne sont jamais touchés
$docsAfter = (Get-ChildItem $docs -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
Check "cours (Documents\Campus Manager) intacts : $docsBefore → $docsAfter fichiers" ($docsBefore -eq $docsAfter)

$results
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Run-AsWindows "cmd /c rmdir /s /q `"$data`""
Remove-Item $data, $dir -Recurse -Force -ErrorAction SilentlyContinue
if ($results | Where-Object { $_ -like 'ÉCHEC*' }) { exit 1 }
