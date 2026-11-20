; Inno Setup script for OmniControl. Version, source and output folder come from tools\release.ps1 (/D defines); nothing is typed here twice.
#ifndef AppVersion
  #error Run through tools\release.ps1
#endif
#define AppGuid "{6F7E1C8A-3B5D-4E2F-9A61-0C0D3B7E5A21}"
[Setup]
AppId={#"{" + AppGuid}
AppName=OmniControl
AppVersion={#AppVersion}
AppPublisher=OmniControl
; For all users in Program Files, where only administrators can change the files: the network service runs OmniControl.exe as SYSTEM.
DefaultDirName={autopf}\OmniControl
DefaultGroupName=OmniControl
PrivilegesRequired=admin
; The settings, the "Start with Windows" entry and the driver's record stay the installing user's own (HKCU, %APPDATA%) on purpose.
UsedUserAreasWarning=no
OutputDir={#OutDir}
OutputBaseFilename=OmniControl-{#AppVersion}-Setup
SetupIconFile={#Root}\OmniControl.App\Assets\OmniControl.ico
UninstallDisplayIcon={app}\OmniControl.exe
LicenseFile={#Root}\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesInstallIn64BitMode=x64compatible
; [Code] asks OmniControl to quit instead of Restart Manager, so its shut-down steps put Windows back.
CloseApplications=no
[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\OmniControl"; Filename: "{app}\OmniControl.exe"
Name: "{autodesktop}\OmniControl"; Filename: "{app}\OmniControl.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
; Installs the bundled driver after the files ([Code] InstallSteps); uninstall removes it only if OmniControl installed it.
Name: packetfilter; Description: "Install Windows Packet Filter (for per-app download limits)"
; Added after the files (the app's --network-service install); left unticked, a service from before is removed; uninstall removes it.
Name: networkservice; Description: "Network service (per-app limits without admin)"
[Run]
; Other PCs reach OmniControl over Tailscale only through this rule (Core/Engine/Tailscale/TailscaleFirewall: same name, port and ranges); Setup is elevated, so no later prompt.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OmniControl on Tailscale"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OmniControl on Tailscale"" dir=in action=allow protocol=TCP localport=47810 remoteip=100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any"; Flags: runhidden waituntilterminated
; As the person who started Setup, not elevated: the app's own "Run as administrator" decides.
Filename: "{app}\OmniControl.exe"; Description: "Start OmniControl"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
; The network blocks a crashed run may have left (OmniControl's own firewall rule group; upload limits end with a restart).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Remove-NetFirewallRule -Group 'OmniControl' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallGroup"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OmniControl on Tailscale"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTailscaleRule"

; ---- Equalizer APO: its official installer (Core/Engine/Apo/ApoInstaller, in {app}\equalizer-apo), run after the files ----
; It is its own program with its own uninstaller and Apps entry, so OmniControl's uninstall leaves it.
[Tasks]
Name: equalizerapo; Description: "Equalizer APO (master effects on your speakers; skipped if already installed)"
[Code]
var EqualizerApoNote: String;

// Its Uninstall entry, or its InstallPath holding EqualizerAPO.dll (retyped from Core/Engine/Apo/ApoIds): its uninstaller leaves
// HKLM\SOFTWARE\EqualizerAPO behind, so the key alone does not count.
function EqualizerApoInstalled(): Boolean;
var Folder: String;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EqualizerAPO')
    or (RegQueryStringValue(HKLM64, 'SOFTWARE\EqualizerAPO', 'InstallPath', Folder) and FileExists(AddBackslash(Folder) + 'EqualizerAPO.dll'));
end;

// NSIS /S skips its pages (its default folder, its update check ticked as its own installer ticks it) but still opens its Device Selector
// and waits for it; a restart its check asks for is never done by a silent NSIS installer, so the finish page says when one may be needed.
procedure InstallEqualizerApo();
var Code: Integer;
begin
  if not WizardIsTaskSelected('equalizerapo') or EqualizerApoInstalled() then Exit;
  WizardForm.StatusLabel.Caption := 'Installing Equalizer APO: tick your speakers in its Device Selector when it opens...';
  Exec(ExpandConstant('{app}\equalizer-apo\EqualizerAPO-x64-1.4.2.exe'), '/S', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code);
  if EqualizerApoInstalled() then
    EqualizerApoNote := 'Equalizer APO is installed. If its check showed a device that didn''t start, restart Windows; its Device Selector (Start menu) changes its devices.'
  else
    EqualizerApoNote := 'Equalizer APO was not installed. OmniControl offers it again when you first use a master effect.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (EqualizerApoNote <> '') then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + EqualizerApoNote;
end;
// ---- end of Equalizer APO ----

[Code]
// The per-user install of 0.2.3 and earlier: its folder and its uninstall entry (Inno's, under HKCU, same AppId).
const
  PerUserFolder = '{localappdata}\Programs\OmniControl';
  PerUserUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';

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

// The network service's name, retyped from Core/Engine/Resources/NetworkServiceSetup (Inno's script cannot read C#).
const
  NetworkServiceName = 'OmniControlNetwork';

function NetworkServiceInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + NetworkServiceName);
end;

// Stops the service and waits a bounded time until it has, so its process is not taken for the app's.
procedure StopNetworkService();
var Code, Tries: Integer;
begin
  if not NetworkServiceInstalled() then Exit;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + NetworkServiceName, '', SW_HIDE, ewWaitUntilTerminated, Code);
  Tries := 60;
  while (Tries > 0) and not (Exec(ExpandConstant('{cmd}'), '/C sc query ' + NetworkServiceName + ' | find "STOPPED"', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0)) do
  begin
    Sleep(250);
    Tries := Tries - 1;
  end;
end;

// Asks OmniControl to quit (this copy's or, on the move from a per-user install, the old one's --quit), so its shut-down steps put Windows
// back. The network service (also OmniControl.exe) is stopped first; it takes its rules out as it stops.
// Never waits on the helper itself: an older installed copy does not know --quit and would sit as a second, hidden copy, so Setup only
// waits a bounded time for the app to go, then ends every OmniControl.exe by force, the helper included.
function EndOmniControl(): String;
var Code: Integer;
    Exe: String;
begin
  Result := '';
  StopNetworkService();
  if not IsRunning() then Exit;
  Exe := ExpandConstant('{app}\OmniControl.exe');
  if not FileExists(Exe) then Exe := ExpandConstant(PerUserFolder + '\OmniControl.exe');
  if FileExists(Exe) and Exec(Exe, '--quit', '', SW_HIDE, ewNoWait, Code) and GoneWithin(60) then Exit;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM OmniControl.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if not GoneWithin(20) then Result := 'OmniControl is still running. Close it from its tray icon, then run Setup again.';
end;

// Windows Packet Filter's names, retyped from Core/Engine/Resources/PacketFilterSetup and PacketFilter (Inno's script cannot read C#).
const
  PacketFilterInf = '{app}\windows-packet-filter\ndisrd_lwf.inf';
  PacketFilterComponent = 'nt_ndisrd';
  PacketFilterRecordKey = 'Software\OmniControl';
  PacketFilterRecordValue = 'PacketFilterInstalled';

// The move from a per-user install: its own uninstaller runs silently (the settings in %APPDATA%\OmniControl stay) with the driver's record
// set aside, so it leaves Windows Packet Filter installed, then its folder goes. Only the usual folder is deleted, never a folder the person
// chose. The sign-in task, the network service and the start-up entry are pointed at this install after the files (InstallSteps).
procedure RemovePerUserCopy();
var Uninstaller, Folder: String;
    Code, Tries: Integer;
    Mine: Cardinal;
    HadRecord: Boolean;
begin
  if not RegQueryStringValue(HKCU, PerUserUninstallKey, 'UninstallString', Uninstaller) then Exit;
  HadRecord := RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine);
  if HadRecord then RegDeleteValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue);
  Exec(RemoveQuotes(Uninstaller), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if HadRecord then RegWriteDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine);
  RegDeleteKeyIncludingSubkeys(HKCU, PerUserUninstallKey);
  Folder := ExpandConstant(PerUserFolder);
  Tries := 20; // the old uninstaller removes its own exe a moment after it ends
  while DirExists(Folder) and not DelTree(Folder, True, True, True) and (Tries > 0) do
  begin
    Sleep(250);
    Tries := Tries - 1;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EndOmniControl();
  if Result = '' then RemovePerUserCopy();
end;

// Removing the filter may leave its file behind, so the service must be there too.
function PacketFilterInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\drivers\ndisrd.sys')) and RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ndisrd');
end;

// Windows' own netcfg (as the driver's own MSI runs it), as one step of RunSteps. The cmd may be 32-bit or 64-bit: Sysnative exists only
// when it is 32-bit, and then picks the 64-bit netcfg.
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

// The network service, added (for this Windows user, named so an administrator's prompt still serves them) or removed by the app itself,
// which owns its definition and refuses a folder others may change; the switch is retyped from the app's Shared/Services/NetworkServiceMode.
function NetworkService(Verb: String): String;
begin
  Result := '("' + ExpandConstant('{app}\OmniControl.exe') + '" --network-service ' + Verb + ')';
end;

// The saved "Run as administrator" (Mouse and Trackpad, retyped from the app's settings file); off for a new install, its default.
function RunsAsAdministrator(): Boolean;
var Text: AnsiString;
begin
  Result := LoadStringFromFile(ExpandConstant('{userappdata}\OmniControl\mouse.json'), Text) and (Pos('"RunAsAdministrator":true', Text) > 0);
end;

// "Start with Windows" (entry name and argument retyped from the app's Shared/Services/StartupRegistration): an entry that is there is
// pointed at this install, so a moved install still starts at sign-in; none is added.
procedure PointStartupEntry();
var Command: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OmniControl', Command) then
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OmniControl', '"' + ExpandConstant('{app}\OmniControl.exe') + '" --background');
end;

// Joins the steps (each in brackets) into one cmd line.
function AddStep(Steps, Step: String): String;
begin
  if Steps = '' then Result := Step else Result := Steps + ' & ' + Step;
end;

// Every step of one run in one hidden cmd, waited for (Setup and uninstall already run as administrator).
procedure RunSteps(Steps: String);
var Code: Integer;
begin
  if Steps <> '' then Exec(ExpandConstant('{cmd}'), '/S /C "' + Steps + '"', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// A driver install that fails leaves OmniControl installed and says so in one line. The record lets uninstall remove only this copy. A
// sign-in task that fails is added by OmniControl's first start as administrator.
procedure InstallSteps();
var Steps: String;
    Driver: Boolean;
begin
  PointStartupEntry();
  Driver := WizardIsTaskSelected('packetfilter') and not PacketFilterInstalled();
  Steps := '';
  if Driver then Steps := NetCfg('-v -l "' + ExpandConstant(PacketFilterInf) + '" -c s -i ' + PacketFilterComponent);
  if RunsAsAdministrator() then Steps := AddStep(Steps, SignInTask('on'));
  if WizardIsTaskSelected('networkservice') then
    Steps := AddStep(Steps, NetworkService('install "' + GetEnv('USERDOMAIN') + '\' + GetUserNameString() + '"'))
  else if NetworkServiceInstalled() then
    Steps := AddStep(Steps, NetworkService('remove'));
  RunSteps(Steps);
  InstallEqualizerApo();
  if not Driver then Exit;
  if PacketFilterInstalled() then
    RegWriteDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, 1)
  else
    SuppressibleMsgBox('Windows Packet Filter was not installed. OmniControl installs it when you first set a download limit.', mbInformation, MB_OK, IDOK);
end;

// Only a driver OmniControl installed (the record) is removed, so another program's is never taken away; a copy that stays keeps its record.
// The sign-in task and the network service go in the same step (the app removed the task already when the setting was turned off).
procedure UninstallSteps();
var Mine: Cardinal;
    Steps: String;
    Driver: Boolean;
begin
  Driver := RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine) and (Mine = 1);
  Steps := '';
  if RunsAsAdministrator() then Steps := SignInTask('off');
  if NetworkServiceInstalled() then Steps := AddStep(Steps, NetworkService('remove'));
  if Driver and PacketFilterInstalled() then Steps := AddStep(Steps, NetCfg('-v -u ' + PacketFilterComponent));
  RunSteps(Steps);
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
  if CurStep = ssPostInstall then InstallSteps();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then UninstallSteps();
end;

function InitializeUninstall(): Boolean;
var Msg: String;
begin
  Msg := EndOmniControl();
  if Msg <> '' then MsgBox(Msg, mbError, MB_OK);
  Result := Msg = '';
end;
