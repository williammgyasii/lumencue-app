; Inno Setup script for LumenCue.
; Build with: installer\build-installer.ps1  (publishes the app, then compiles this script)
; Or manually: ISCC.exe /DMyAppVersion=0.5.0 installer\ChurchProjection.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.5.0"
#endif

#define MyAppName "LumenCue"
#define MyAppPublisher "LumenCue"
#define MyAppExeName "ChurchProjection.App.exe"
; Self-contained publish output (created by build-installer.ps1), relative to this .iss file.
#define SourceDir "..\publish\app"

[Setup]
; Keep this AppId stable across versions so upgrades replace the prior install.
AppId={{8F3C5E1A-7B42-4D9E-9C16-2E0A6B3F1D77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\publish
OutputBaseFilename=LumenCue-Setup-{#MyAppVersion}
SetupIconFile=..\src\ChurchProjection.App\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
