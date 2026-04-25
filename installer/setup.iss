[Setup]
AppName=DirHealth
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
AppVersion={#AppVersion}
AppPublisher=DirHealth
AppPublisherURL=https://dirhealth.app
AppSupportURL=https://dirhealth.app
DefaultDirName={autopf}\DirHealth
DefaultGroupName=DirHealth
OutputDir=Output
OutputBaseFilename=DirHealth-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\DirHealth.exe
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\DirHealth.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\DirHealth";       Filename: "{app}\DirHealth.exe"
Name: "{group}\Uninstall DirHealth"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DirHealth"; Filename: "{app}\DirHealth.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\DirHealth"; Flags: uninsdeletekey

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\ADHygiene"
Type: filesandordirs; Name: "{commonappdata}\ADHygiene"
Type: filesandordirs; Name: "{userappdata}\DirHealth"

[Run]
Filename: "{app}\DirHealth.exe"; Description: "{cm:LaunchProgram,DirHealth}"; Flags: nowait postinstall skipifsilent
