; lil agents - Inno Setup installer script
; Per-user install, no admin required

#define MyAppName "lil agents"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "lil agents"
#define MyAppURL "https://lilagents.xyz"
#define MyAppExeName "LilAgents.exe"
#define MyAppId "com.lilagents.app"

; Publish output path (relative to this .iss file)
#define PublishDir "..\LilAgents\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish"

[Setup]
AppId={{B7E3A2F1-4D8C-4F6E-9A1B-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\LilAgents
DefaultGroupName={#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=Output
OutputBaseFilename=LilAgentsSetup-{#MyAppVersion}
SetupIconFile=..\LilAgents\Assets\Icons\trayicon-dark.ico
UninstallDisplayIcon={app}\LilAgents.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
CloseApplicationsFilter=LilAgents.exe
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Add to Windows startup if task is selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LilAgents"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Close running instances before install/upgrade
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Attempt to close running instances gracefully
  Exec('taskkill.exe', '/f /im LilAgents.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Small delay to ensure process has exited
  Sleep(500);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Close running instances before uninstall
    Exec('taskkill.exe', '/f /im LilAgents.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;
