; Tagster installer script for Inno Setup 6+.
; Build the app first (see docs/PACKAGING.md), then compile this with:
;     iscc installer\Tagster.iss
;
; The Explorer right-click menu is registered per-user from inside the app
; (Settings), so this installer makes no registry changes.

#define MyAppName "Tagster"
#define MyAppVersion "0.2.1"
#define MyAppPublisher "Tagster"
#define MyAppExeName "Tagster.exe"

; Folder produced by `dotnet publish`. Override with /DPublishDir=... if needed.
#ifndef PublishDir
  #define PublishDir "..\src\Tagster.App\bin\Release\net10.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{B7E6F3C2-1A4D-4C8E-9F2B-2E5A9D7C4F10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\Tagster.App\Tagster.ico
OutputDir=dist
OutputBaseFilename=Tagster-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=lowest

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallRun]
; Remove the per-user right-click menu entries (if the user enabled them) before files go.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "TagsterUnregister"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Tagster"; Flags: nowait postinstall skipifsilent
