; Inno Setup script for OmniControl. Version, source and output folder come from tools\release.ps1 (/D defines); nothing is typed here twice.
#ifndef AppVersion
  #error Run through tools\release.ps1
#endif
[Setup]
AppId={{6F7E1C8A-3B5D-4E2F-9A61-0C0D3B7E5A21}
AppName=OmniControl
AppVersion={#AppVersion}
AppPublisher=OmniControl
DefaultDirName={localappdata}\Programs\OmniControl
DefaultGroupName=OmniControl
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=OmniControl-{#AppVersion}-Setup
SetupIconFile={#Root}\OmniControl.App\Assets\OmniControl.ico
UninstallDisplayIcon={app}\OmniControl.exe
LicenseFile={#Root}\LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\OmniControl"; Filename: "{app}\OmniControl.exe"
Name: "{userdesktop}\OmniControl"; Filename: "{app}\OmniControl.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Run]
Filename: "{app}\OmniControl.exe"; Description: "Start OmniControl"; Flags: nowait postinstall skipifsilent
