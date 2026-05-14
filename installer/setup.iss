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
; Branded wizard images (generated from app icon + dark theme palette)
WizardImageFile=assets\wizard-banner.png
WizardSmallImageFile=assets\wizard-small.png
UninstallDisplayIcon={app}\DirHealth.exe
PrivilegesRequired=lowest
; Built-in setup logging (writes to %TEMP%\Setup Log *.txt; we also write our own log below)
SetupLogging=yes
; Auto-detect and offer to close running DirHealth before install
CloseApplications=yes
; Embed version info in the setup .exe itself
VersionInfoVersion={#AppVersion}
VersionInfoDescription=DirHealth Setup
VersionInfoCompany=DirHealth

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\DirHealth.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\DirHealth";           Filename: "{app}\DirHealth.exe"
Name: "{group}\Uninstall DirHealth"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DirHealth";     Filename: "{app}\DirHealth.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\DirHealth"; Flags: uninsdeletekey
; Track installed version so upgrade detection works across reinstalls
Root: HKCU; Subkey: "SOFTWARE\DirHealth"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[UninstallDelete]
; Remove legacy ADHygiene folders left over from the old app name
Type: filesandordirs; Name: "{userappdata}\ADHygiene"
Type: filesandordirs; Name: "{commonappdata}\ADHygiene"
; %APPDATA%\DirHealth\ is intentionally NOT deleted — scan history and settings are preserved

[Run]
; Launch DirHealth after install (interactive) or silently (auto-update path)
Filename: "{app}\DirHealth.exe"; Description: "{cm:LaunchProgram,DirHealth}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\DirHealth.exe"; Flags: nowait; Check: WizardSilent
; Optional log viewer on finish page (unchecked by default)
Filename: "notepad.exe"; Parameters: """{userappdata}\DirHealth\install.log"""; Description: "View installation log"; Flags: postinstall skipifsilent unchecked

[Code]
var
  ExplainerPage: TOutputMsgWizardPage;
  InstallLog: TStringList;
  InstallLogPath: String;
  SetupDone: Boolean;

// --- Install log: written to %APPDATA%\DirHealth\install.log ---

procedure WriteInstallLog(const Msg: String);
begin
  if InstallLog <> nil then
    InstallLog.Add(GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + '  ' + Msg);
  Log(Msg);
end;

procedure FlushInstallLog;
begin
  if (InstallLog = nil) or (InstallLogPath = '') then Exit;
  try
    ForceDirectories(ExtractFilePath(InstallLogPath));
    InstallLog.SaveToFile(InstallLogPath);
  except
    on E: Exception do
      Log('WARNING: Could not write install log: ' + E.Message);
  end;
end;

// --- Previous version detection via registry ---

function GetPreviousVersion: String;
var
  V: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'SOFTWARE\DirHealth', 'Version', V) then
    Result := V;
end;

// --- Initialization ---

function InitializeSetup: Boolean;
var
  PrevVersion: String;
begin
  Result := True;
  SetupDone := False;
  InstallLogPath := ExpandConstant('{userappdata}\DirHealth\install.log');
  InstallLog := TStringList.Create;

  WriteInstallLog('=== DirHealth Setup {#AppVersion} ===');
  WriteInstallLog('OS: ' + GetWindowsVersionString);
  WriteInstallLog('User: ' + GetUserNameString);

  PrevVersion := GetPreviousVersion;
  if PrevVersion <> '' then
    WriteInstallLog('Upgrading from: v' + PrevVersion)
  else
    WriteInstallLog('Fresh installation');
end;

procedure InitializeWizard;
var
  PrevVersion: String;
begin
  // Permission explainer — shown right after the welcome page
  ExplainerPage := CreateOutputMsgPage(
    wpWelcome,
    'How DirHealth Works',
    'Permissions and data access',
    'DirHealth connects to Active Directory in READ-ONLY mode.' + #13#10 +
    'It never modifies users, groups, computers, or any AD objects.' + #13#10#13#10 +
    'Stored locally in %APPDATA%\DirHealth\:' + #13#10 +
    '  - Encrypted credentials (AES-256)' + #13#10 +
    '  - Scan cache and hygiene score history' + #13#10 +
    '  - Acknowledged findings' + #13#10#13#10 +
    'No telemetry. No licensing. No external servers.'
  );

  // Show upgrade notice on the welcome page itself
  PrevVersion := GetPreviousVersion;
  if PrevVersion <> '' then
    WizardForm.WelcomeLabel2.Caption :=
      'This will upgrade DirHealth from v' + PrevVersion +
      ' to v{#AppVersion}.' + #13#10 + #13#10 +
      'Your scan history and settings in %APPDATA%\DirHealth\ are preserved.';
end;

// --- Installation steps ---

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
    begin
      WizardForm.StatusLabel.Caption := 'Preparing installation...';
      WriteInstallLog('Installation started');
    end;

    ssPostInstall:
    begin
      WizardForm.StatusLabel.Caption := 'Finalizing installation...';
      WriteInstallLog('Files installed successfully');
      FlushInstallLog;
    end;

    ssDone:
    begin
      SetupDone := True;
      WriteInstallLog('=== Setup completed ===');
      FlushInstallLog;
    end;
  end;
end;

procedure CurFileChanged(const FileName: String);
begin
  if FileName <> '' then
    WriteInstallLog('Installing: ' + ExtractFileName(FileName));
end;

// --- Uninstall: inform user that scan data is preserved ---

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox(
      'DirHealth has been uninstalled.' + #13#10 + #13#10 +
      'Your scan history and settings are preserved here:' + #13#10 +
      ExpandConstant('{userappdata}\DirHealth') + #13#10 + #13#10 +
      'You can safely delete this folder if you no longer need the data.',
      mbInformation, MB_OK
    );
end;

// --- Cleanup ---

procedure DeinitializeSetup;
begin
  if not SetupDone then
  begin
    WriteInstallLog('=== Setup did not complete ===');
    FlushInstallLog;
  end;
  if InstallLog <> nil then
  begin
    InstallLog.Free;
    InstallLog := nil;
  end;
end;
