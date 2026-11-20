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
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesInstallIn64BitMode=x64compatible
; OmniControl may run as admin, so this non-admin setup cannot close it through Restart Manager; [Code] asks it to quit instead.
CloseApplications=no
[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\OmniControl"; Filename: "{app}\OmniControl.exe"
Name: "{userdesktop}\OmniControl"; Filename: "{app}\OmniControl.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
; Installs the bundled driver after the files ([Code] InstallElevated); uninstall removes it only if OmniControl installed it.
Name: packetfilter; Description: "Install Windows Packet Filter (for per-app download limits)"
[Run]
; shellexec: the app asks for admin, which a plain CreateProcess from a non-admin setup cannot start.
Filename: "{app}\OmniControl.exe"; Description: "Start OmniControl"; Flags: nowait postinstall skipifsilent shellexec
[UninstallRun]
; The network blocks a crashed run may have left (OmniControl's own firewall rule group; upload limits end with a restart). Admin prompt.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Remove-NetFirewallRule -Group 'OmniControl' -ErrorAction SilentlyContinue"""; Flags: shellexec runhidden waituntilterminated; Verb: "runas"; RunOnceId: "RemoveFirewallGroup"

; ---- OmniControl's own Equalizer APO (Core/Engine/Apo/ApoSetup): installed in Setup's one admin step, removed by uninstall ----
[Tasks]
Name: equalizerapo; Description: "Equalizer APO (master effects on your speakers; skipped automatically if already installed)"
[Code]
// One step of the app's elevated helper ("--equalizer-apo install|remove"); the app itself never changes a separately installed Equalizer APO.
function EqualizerApo(Verb: String): String;
begin
  Result := '("' + ExpandConstant('{app}\OmniControl.exe') + '" --equalizer-apo ' + Verb + ')';
end;

// OmniControl's marker (retyped from Core/Engine/Apo/ApoIds OwnMarkerKey and OwnPathValue): only then does uninstall need the removal.
function EqualizerApoOurs(): Boolean;
begin
  Result := RegValueExists(HKLM64, 'SOFTWARE\OmniControl\EqualizerApo', 'Path');
end;
// ---- end of Equalizer APO ----

[Code]
function IsRunning(): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq OmniControl.exe" /NH | find /I "OmniControl.exe"', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function GoneWithin(Tries: Integer): Boolean;
begin
  while IsRunning() and (Tries > 0) do begin Sleep(250); Tries := Tries - 1; end;
  Result := not IsRunning();
end;

// Asks OmniControl to quit (elevated, one admin prompt: it usually runs as administrator), so its shut-down steps put Windows back.
// Never waits on the helper itself: an older installed copy does not know --quit and would sit as a second, hidden copy, so Setup only
// waits a bounded time for the app to go, then ends every OmniControl.exe by force (one admin prompt), the helper included.
function EndOmniControl(): String;
var Code: Integer;
    Exe: String;
begin
  Result := '';
  if not IsRunning() then Exit;
  Exe := ExpandConstant('{app}\OmniControl.exe');
  if FileExists(Exe) and ShellExec('runas', Exe, '--quit', '', SW_HIDE, ewNoWait, Code) and GoneWithin(60) then Exit;
  ShellExec('runas', 'taskkill.exe', '/F /IM OmniControl.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if not GoneWithin(20) then Result := 'OmniControl is still running. Close it from its tray icon, then run Setup again.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EndOmniControl();
end;

// Windows Packet Filter's names, retyped from Core/Engine/Resources/PacketFilterSetup and PacketFilter (Inno's script cannot read C#).
const
  PacketFilterInf = '{app}\windows-packet-filter\ndisrd_lwf.inf';
  PacketFilterComponent = 'nt_ndisrd';
  PacketFilterRecordKey = 'Software\OmniControl';
  PacketFilterRecordValue = 'PacketFilterInstalled';
  AdminPromptRefused = 1223;

// Removing the filter may leave its file behind, so the service must be there too.
function PacketFilterInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\drivers\ndisrd.sys')) and RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ndisrd');
end;

// Windows' own netcfg (as the driver's own MSI runs it), as one step of RunElevated. ShellExec never turns WOW64 redirection off, so the
// elevated cmd picks the 64-bit netcfg: Sysnative exists only when cmd itself is 32-bit.
function NetCfg(Args: String): String;
var Exe: String;
begin
  Exe := '"%SystemRoot%\Sysnative\netcfg.exe"';
  Result := '(if exist ' + Exe + ' (' + Exe + ' ' + Args + ') else ("%SystemRoot%\System32\netcfg.exe" ' + Args + '))';
end;

// OmniControl's sign-in task (Run as administrator without a prompt), added or removed by the app itself, which owns its name and settings.
function SignInTask(OnOff: String): String;
begin
  Result := '("' + ExpandConstant('{app}\OmniControl.exe') + '" --sign-in-task ' + OnOff + ')';
end;

// The saved "Run as administrator" (Mouse and Trackpad, retyped from the app's settings file); off for a new install, its default.
function RunsAsAdministrator(): Boolean;
var Text: AnsiString;
begin
  Result := LoadStringFromFile(ExpandConstant('{userappdata}\OmniControl\mouse.json'), Text) and (Pos('"RunAsAdministrator":true', Text) > 0);
end;

// Joins the elevated steps (each in brackets) into one cmd line.
function AddStep(Steps, Step: String): String;
begin
  if Steps = '' then Result := Step else Result := Steps + ' & ' + Step;
end;

// Every elevated step of one run behind one visible admin prompt, waited for; the ShellExec error code, 0 when it ran (nothing to do: 0).
function RunElevated(Steps: String): Integer;
begin
  Result := 0;
  if Steps = '' then Exit;
  if not ShellExec('runas', ExpandConstant('{cmd}'), '/S /C "' + Steps + '"', '', SW_HIDE, ewWaitUntilTerminated, Result) then Exit;
  Result := 0;
end;

// A driver install that fails leaves OmniControl installed and says so in one line; a refused prompt says nothing. The record lets uninstall
// remove only this copy. A sign-in task that fails is added by OmniControl's first start as administrator.
procedure InstallElevated();
var Steps: String;
    Driver: Boolean;
begin
  Driver := WizardIsTaskSelected('packetfilter') and not PacketFilterInstalled();
  Steps := '';
  if Driver then Steps := NetCfg('-v -l "' + ExpandConstant(PacketFilterInf) + '" -c s -i ' + PacketFilterComponent);
  if RunsAsAdministrator() then Steps := AddStep(Steps, SignInTask('on'));
  if WizardIsTaskSelected('equalizerapo') then Steps := AddStep(Steps, EqualizerApo('install'));
  if (RunElevated(Steps) = AdminPromptRefused) or not Driver then Exit;
  if PacketFilterInstalled() then
    RegWriteDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, 1)
  else
    SuppressibleMsgBox('Windows Packet Filter was not installed. OmniControl installs it when you first set a download limit.', mbInformation, MB_OK, IDOK);
end;

// Only a driver OmniControl installed (the record) is removed, so another program's is never taken away; a copy that stays keeps its record.
// The sign-in task goes in the same prompt (the app removed it already when the setting was turned off).
procedure UninstallElevated();
var Mine: Cardinal;
    Steps: String;
    Driver: Boolean;
begin
  Driver := RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine) and (Mine = 1);
  Steps := '';
  if RunsAsAdministrator() then Steps := SignInTask('off');
  if Driver and PacketFilterInstalled() then Steps := AddStep(Steps, NetCfg('-v -u ' + PacketFilterComponent));
  if EqualizerApoOurs() then Steps := AddStep(Steps, EqualizerApo('remove'));
  RunElevated(Steps);
  if not Driver then Exit;
  if PacketFilterInstalled() then
    SuppressibleMsgBox('Windows Packet Filter was not removed. Installing and uninstalling OmniControl again removes it.', mbInformation, MB_OK, IDOK)
  else
  begin
    RegDeleteValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue);
    RegDeleteKeyIfEmpty(HKCU, PacketFilterRecordKey);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallElevated();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then UninstallElevated();
end;

function InitializeUninstall(): Boolean;
var Msg: String;
begin
  Msg := EndOmniControl();
  if Msg <> '' then MsgBox(Msg, mbError, MB_OK);
  Result := Msg = '';
end;
