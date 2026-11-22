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
