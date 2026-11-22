; Installateur de Campus Manager (Inno Setup 6). Construit par installer\build.ps1, qui passe /DAppVersion=x.y.z.
; Installation par utilisateur : aucun droit administrateur.

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

[Setup]
AppId={{3B8F6C21-9D4E-4A7B-8E25-6C1F0D9A7B43}
AppName=Campus Manager
AppVersion={#AppVersion}
AppVerName=Campus Manager {#AppVersion}
AppPublisher=Campus Manager
DefaultDirName={userpf}\Campus Manager
DefaultGroupName=Campus Manager
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=Campus Manager Setup {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
SetupIconFile=..\build\icon.ico
UninstallDisplayIcon={app}\Campus Manager.exe
UninstallDisplayName=Campus Manager
AppMutex=Local\CampusManager.Instance
CloseApplications=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Campus Manager
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=Installateur de Campus Manager

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer une icône sur le Bureau"

[Files]
Source: "..\publish\{#AppVersion}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Campus Manager"; Filename: "{app}\Campus Manager.exe"
Name: "{autodesktop}\Campus Manager"; Filename: "{app}\Campus Manager.exe"; Tasks: desktopicon

[Registry]
; Écrite par Campus Manager lui-même (« Démarrage de Windows ») : jamais laissée derrière une désinstallation.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Campus Manager"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Campus Manager.exe"; Description: "Lancer Campus Manager"; Flags: nowait postinstall

[Code]
// --- Ancienne version 0.1 (Electron) -------------------------------------------------------------
// Elle s'installait ailleurs (Programs\campus-manager) : on la désinstalle avant d'installer celle-ci,
// sinon les deux versions se disputeraient le démarrage avec Windows. Son désinstalleur garde toujours
// la base (%APPDATA%\Campus Manager) et les cours (Documents\Campus Manager) : rien n'est perdu.
function RemoveOldElectron(): String;
var
  Dir, Unins: String;
  Code, I: Integer;
begin
  Result := '';
  Dir := ExpandConstant('{localappdata}\Programs\campus-manager');
  Unins := Dir + '\Uninstall Campus Manager.exe';
  if not FileExists(Unins) then Exit;
  Log('Ancienne version 0.1 trouvée : désinstallation.');
  // L'ancienne appli doit être fermée (seulement ses processus, repérés par leur dossier).
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "Get-Process | Where-Object { $_.Path -like ''*\Programs\campus-manager\*'' } | Stop-Process -Force"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(Unins, '/S', Dir, SW_HIDE, ewWaitUntilTerminated, Code);
  // Le désinstalleur se relance depuis un dossier temporaire : on attend que le programme ait disparu.
  for I := 1 to 120 do
  begin
    if not FileExists(Dir + '\Campus Manager.exe') then Break;
    Sleep(500);
  end;
  if FileExists(Dir + '\Campus Manager.exe') then
    Result := 'L''ancienne version 0.1 de Campus Manager n''a pas pu être désinstallée.' + #13#10 +
              'Désinstalle-la depuis Paramètres > Applications (tes cours et ta base sont gardés), puis relance cette installation.'
  else
    Log('Ancienne version 0.1 désinstallée.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := RemoveOldElectron();
end;
{ La base (devoirs, réglages), le jeton chiffré et les journaux sont dans %APPDATA%\Campus Manager.
  Ils sont gardés par défaut ; une désinstallation silencieuse ne les supprime qu'avec /DELETEDATA=1.
  Les cours téléchargés (Documents\Campus Manager) ne sont JAMAIS supprimés : ce sont tes fichiers. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  Remove: Boolean;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  DataDir := ExpandConstant('{userappdata}\Campus Manager');
  if not DirExists(DataDir) then Exit;
  if UninstallSilent then
    Remove := ExpandConstant('{param:DELETEDATA|0}') = '1'
  else
    Remove := MsgBox('Supprimer aussi la base de Campus Manager (devoirs, favoris, réglages, connexion) ?' + #13#10 + #13#10 +
                     'Choisissez Non pour les garder en vue d''une réinstallation. Tes cours dans Documents\Campus Manager ne sont jamais touchés.',
                     mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if Remove then DelTree(DataDir, True, True, True);
end;
