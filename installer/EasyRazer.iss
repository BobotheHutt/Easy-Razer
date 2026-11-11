; Easy Razer installer (Inno Setup 6). Built by .github/workflows/build.yml:
;   iscc /DAppVersion=0.1.42 /DSourceExe=..\publish\EasyRazer.exe installer\EasyRazer.iss
;
; Installs per user (no admin prompt) into %LOCALAPPDATA%\Programs\Easy Razer, which is also what
; lets the app update itself silently. Settings in %APPDATA%\EasyRazer are kept on uninstall.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\EasyRazer.exe"
#endif

[Setup]
AppId={{7E3B2C61-4F0A-4C2B-9B8E-3D1E5A9C2F47}
AppName=Easy Razer
AppVersion={#AppVersion}
AppVerName=Easy Razer {#AppVersion}
AppPublisher=Easy Razer
DefaultDirName={localappdata}\Programs\Easy Razer
DefaultGroupName=Easy Razer
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\publish
OutputBaseFilename=EasyRazer-Setup
SetupIconFile=..\assets\EasyRazer.ico
UninstallDisplayIcon={app}\EasyRazer.exe
UninstallDisplayName=Easy Razer
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running copy before replacing the exe (updates exit the app themselves first).
CloseApplications=force
RestartApplications=no

[Tasks]
Name: "autostart"; Description: "Start Easy Razer when I sign in to Windows"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Easy Razer"; Filename: "{app}\EasyRazer.exe"
Name: "{autodesktop}\Easy Razer"; Filename: "{app}\EasyRazer.exe"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" switch uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EasyRazer"; \
  ValueData: """{app}\EasyRazer.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Normal install: offer to start it. Silent update: start it again in the tray.
Filename: "{app}\EasyRazer.exe"; Description: "Start Easy Razer"; Flags: nowait postinstall skipifsilent
Filename: "{app}\EasyRazer.exe"; Parameters: "--tray --updated"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM EasyRazer.exe /F"; Flags: runhidden; RunOnceId: "StopEasyRazer"

[UninstallDelete]
Type: dirifempty; Name: "{app}"
