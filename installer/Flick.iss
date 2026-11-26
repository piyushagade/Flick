; Flick — Gestures, Chords & Sneak for Windows 11
; Inno Setup 6 Installer Script

#define MyAppName "Flick"
#define MyAppFullName "Flick — Gestures, Chords & Sneak for Windows 11"
#define MyAppPublisher "Flick"
#define MyAppURL "https://github.com/piyushagade/Flick"
#define MyAppExeName "Flick.App.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif

#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "1.3.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\release"
#endif

[Setup]
AppId={{B4E8A1D2-7C39-4E5F-9A1B-3C8D6E2F0A9C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductName={#MyAppFullName}
VersionInfoProductVersion={#MyAppNumericVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppFullName} Setup
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=Flick-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\src\Flick.App\Assets\flick.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppFullName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start Flick automatically when Windows starts"; GroupDescription: "System Integration:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure StopFlickProcesses();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopFlickProcesses();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopFlickProcesses();
  end;
end;
