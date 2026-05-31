; Inno Setup script for Voice to Text.
; Builds a per-user installer (no admin required) from the published output.
; Build:  iscc installer\VoiceToText.iss   (after running publish.ps1)

#define MyAppName "Voice to Text"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Luke Madigan"
#define MyAppExeName "VoiceToText.exe"

[Setup]
AppId={{B7E1C3A2-5F4D-4E8B-9C2A-1D6F3A8B4E20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VoiceToText
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=VoiceToText-Setup
SetupIconFile=..\src\VoiceToText\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Use Restart Manager to close a running instance before updating files.
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start {#MyAppName} automatically when I log in"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Start-on-login (per-user Run key). Mirrors the in-app setting; removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "VoiceToText"; ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
