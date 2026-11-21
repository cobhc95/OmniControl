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
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OmniControl on Tailscale"" dir=in action=allow program=""{app}\OmniControl.exe"" protocol=TCP localport=47810 remoteip=100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any"; Flags: runhidden waituntilterminated
; As the person who started Setup, not elevated: the app's own "Run as administrator" decides.
Filename: "{app}\OmniControl.exe"; Description: "Start OmniControl"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
; The network blocks a crashed run may have left (OmniControl's own firewall rule group; upload limits end with a restart).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Remove-NetFirewallRule -Group 'OmniControl' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallGroup"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OmniControl on Tailscale"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTailscaleRule"

; ---- Equalizer APO: its official installer (Core/Engine/Apo/ApoInstaller, in {app}\equalizer-apo), run after the files ----
; It is its own program with its own uninstaller and Apps entry; OmniControl's uninstall runs that uninstaller when its box stays ticked.
[Tasks]
Name: equalizerapo; Description: "Equalizer APO (master effects on your speakers; skipped if already installed)"
[Code]
var EqualizerApoNote: String;

const
  EqualizerApoUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EqualizerAPO';

// Its Uninstall entry, or its InstallPath holding EqualizerAPO.dll (retyped from Core/Engine/Apo/ApoIds): its uninstaller leaves
// HKLM\SOFTWARE\EqualizerAPO behind, so the key alone does not count.
function EqualizerApoInstalled(): Boolean;
var Folder: String;
begin
  Result := RegKeyExists(HKLM64, EqualizerApoUninstallKey)
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

// Its own uninstaller, named by its Uninstall entry (else its InstallPath), run only from inside 64-bit Program Files, where only
// administrators can change files (this runs elevated). NSIS's _?= (last, unquoted) runs it in place and waited for: without it NSIS
// copies itself to the user-writable %TEMP% and returns at once; in place it leaves its own exe, so that and the emptied folder go after.
procedure UninstallEqualizerApo();
var Uninstaller, ProgramFiles: String;
    Code: Integer;
begin
  if RegQueryStringValue(HKLM64, EqualizerApoUninstallKey, 'UninstallString', Uninstaller) then
    Uninstaller := RemoveQuotes(Uninstaller)
  else if RegQueryStringValue(HKLM64, 'SOFTWARE\EqualizerAPO', 'InstallPath', Uninstaller) then
    Uninstaller := AddBackslash(Uninstaller) + 'Uninstall.exe';
  if Uninstaller <> '' then Uninstaller := ExpandFileName(Uninstaller);
  ProgramFiles := AddBackslash(ExpandConstant('{commonpf64}'));
  if (Uninstaller <> '') and (CompareText(Copy(Uninstaller, 1, Length(ProgramFiles)), ProgramFiles) = 0) and FileExists(Uninstaller) then
  begin
    Exec(Uninstaller, '/S _?=' + ExtractFileDir(Uninstaller), ExtractFileDir(Uninstaller), SW_HIDE, ewWaitUntilTerminated, Code);
    if not EqualizerApoInstalled() then
    begin
      DeleteFile(Uninstaller);
      RemoveDir(ExtractFileDir(Uninstaller));
    end;
  end;
  if EqualizerApoInstalled() then
    SuppressibleMsgBox('Equalizer APO was not removed. Its own uninstaller is in Windows'' Apps list.', mbInformation, MB_OK, IDOK);
end;
// ---- end of Equalizer APO ----

[Code]
// The per-user install of 0.2.3 and earlier: its folder and its uninstall entry (Inno's, under HKCU, same AppId).
const
  PerUserFolder = '{localappdata}\Programs\OmniControl';
  PerUserUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';

// Windows' own tool in System32 run straight and its output searched: never through cmd, whose elevated start would run the person's
// AutoRun command (HKCU\Software\Microsoft\Command Processor).
function ToolSays(Tool, Args, Text: String): Boolean;
var Code, I: Integer;
    Output: TExecOutput;
begin
  Result := False;
  if not ExecAndCaptureOutput(ExpandConstant('{sys}\' + Tool), Args, '', SW_HIDE, ewWaitUntilTerminated, Code, Output) then Exit;
  for I := 0 to GetArrayLength(Output.StdOut) - 1 do
    if Pos(Uppercase(Text), Uppercase(Output.StdOut[I])) > 0 then begin Result := True; Exit; end;
end;

function IsRunning(): Boolean;
begin
  Result := ToolSays('tasklist.exe', '/FI "IMAGENAME eq OmniControl.exe" /NH /FO CSV', '"OmniControl.exe"');
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
  while (Tries > 0) and NetworkServiceInstalled() and not ToolSays('sc.exe', 'query ' + NetworkServiceName, 'STOPPED') do
  begin
    Sleep(250);
    Tries := Tries - 1;
  end;
end;

// The running copy's quit event, retyped from the app's Shared/Services/SingleInstance: set here directly, so no OmniControl.exe (least of
// all one in the old per-user folder, which anything running as the user could replace) is run elevated to ask.
const
  QuitEventName = 'Local\OmniControl.Quit';
  EventModifyState = $0002;

function OpenEvent(Access: Cardinal; Inherit: Integer; Name: String): THandle; external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Integer; external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer; external 'CloseHandle@kernel32.dll stdcall';

function AskToQuit(): Boolean;
var Event: THandle;
begin
  Event := OpenEvent(EventModifyState, 0, QuitEventName);
  Result := (Event <> 0) and (SetEvent(Event) <> 0);
  if Event <> 0 then CloseHandle(Event);
end;

// Asks OmniControl to quit through its quit event, so its shut-down steps put Windows back. The network service (also OmniControl.exe) is
// stopped first; it takes its rules out as it stops. A copy without the event (an older version) or one that does not go within a bounded
// time is ended by force.
function EndOmniControl(): String;
var Code: Integer;
begin
  Result := '';
  StopNetworkService();
  if not IsRunning() then Exit;
  if AskToQuit() and GoneWithin(60) then Exit;
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
// chose, and only the uninstaller in that folder is run, and only with the token of the person who started Setup, never elevated: that
// folder and the per-user Uninstall entry are the person's own, so anything running as them could have replaced it. The sign-in task, the
// network service and the start-up entry are pointed at this install after the files (InstallSteps).
procedure RemovePerUserCopy();
var Uninstaller, Folder: String;
    Code, Tries: Integer;
    Mine: Cardinal;
    HadRecord: Boolean;
begin
  if not RegKeyExists(HKCU, PerUserUninstallKey) then Exit;
  Folder := ExpandConstant(PerUserFolder);
  Uninstaller := Folder + '\unins000.exe';
  HadRecord := RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine);
  if HadRecord then RegDeleteValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue);
  if FileExists(Uninstaller) then ExecAsOriginalUser(Uninstaller,'/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  if HadRecord then RegWriteDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine);
  RegDeleteKeyIncludingSubkeys(HKCU, PerUserUninstallKey);
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

// Runs the installed app with these arguments, hidden and waited for, straight (no cmd, so a user or folder name is never read as cmd syntax):
// its sign-in task (Run as administrator without a prompt; --sign-in-task on|off) and its network service (--network-service install "<user>"|remove,
// for this Windows user, named so an administrator's prompt still serves them; the switch is retyped from the app's Shared/Services/NetworkServiceMode).
// The app owns their names and definitions and refuses a folder others may change.
procedure RunApp(Args: String);
var Code: Integer;
begin
  Exec(ExpandConstant('{app}\OmniControl.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
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

// Windows' own netcfg (as the driver's own MSI runs it), hidden and waited for (Setup and uninstall already run as administrator). Setup
// runs in 64-bit mode, so {sys} is the 64-bit System32 and Exec starts the 64-bit netcfg.
procedure RunNetCfg(Args: String);
var Code: Integer;
begin
  Exec(ExpandConstant('{sys}\netcfg.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// A driver install that fails leaves OmniControl installed and says so in one line. The record lets uninstall remove only this copy. A
// sign-in task that fails is added by OmniControl's first start as administrator.
procedure InstallSteps();
var Driver: Boolean;
begin
  PointStartupEntry();
  Driver := WizardIsTaskSelected('packetfilter') and not PacketFilterInstalled();
  if Driver then RunNetCfg('-v -l "' + ExpandConstant(PacketFilterInf) + '" -c s -i ' + PacketFilterComponent);
  if RunsAsAdministrator() then RunApp('--sign-in-task on');
  if WizardIsTaskSelected('networkservice') then
    RunApp('--network-service install "' + GetEnv('USERDOMAIN') + '\' + GetUserNameString() + '"')
  else if NetworkServiceInstalled() then
    RunApp('--network-service remove');
  InstallEqualizerApo();
  if not Driver then Exit;
  if PacketFilterInstalled() then
    RegWriteDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, 1)
  else
    SuppressibleMsgBox('Windows Packet Filter was not installed. OmniControl installs it when you first set a download limit.', mbInformation, MB_OK, IDOK);
end;

// Only a driver OmniControl installed (the record) is removed, so another program's is never taken away.
function PacketFilterIsOurs(): Boolean;
var Mine: Cardinal;
begin
  Result := RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine) and (Mine = 1);
end;

// The uninstall choices; both on unless /KEEPDRIVERS is passed, also for a silent uninstall, which shows no form.
var RemovingPacketFilter, RemovingEqualizerApo: Boolean;

function KeepsDrivers(): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/KEEPDRIVERS') = 0 then Result := True;
end;

function AddLine(Form: TSetupForm; Text: String; Box: Boolean; var Top: Integer): TControl;
var Line: TNewStaticText;
    Check: TNewCheckBox;
begin
  if Box then
  begin
    Check := TNewCheckBox.Create(Form);
    Check.Caption := Text;
    Check.Height := ScaleY(17);
    Result := Check;
  end else
  begin
    Line := TNewStaticText.Create(Form);
    Line.AutoSize := True;
    Line.WordWrap := True;
    Line.Width := Form.ClientWidth - ScaleX(2 * 12);
    Line.Caption := Text;
    Result := Line;
  end;
  Result.Parent := Form;
  Result.Left := ScaleX(12);
  Result.Top := Top;
  Result.Width := Form.ClientWidth - ScaleX(2 * 12);
  Top := Top + Result.Height + ScaleY(8);
end;

// Inno's uninstaller has no Tasks page, so a small form of its own asks before Inno's confirmation, one box for each part that is there.
// False when it is cancelled.
function ChooseWhatToRemove(): Boolean;
var Form: TSetupForm;
    PacketBox, ApoBox: TNewCheckBox;
    Next, Cancel: TNewButton;
    Top: Integer;
begin
  Result := True;
  RemovingPacketFilter := not KeepsDrivers();
  RemovingEqualizerApo := RemovingPacketFilter;
  PacketBox := nil;
  ApoBox := nil;
  if UninstallSilent() or not ((PacketFilterIsOurs() and PacketFilterInstalled()) or EqualizerApoInstalled()) then Exit;
  Form := CreateCustomForm(ScaleX(360), ScaleY(160), False, True);
  try
    Form.Caption := FmtMessage(SetupMessage(msgUninstallAppFullTitle), ['OmniControl']);
    Top := ScaleY(12);
    AddLine(Form, 'Untick anything you want to keep on this PC.', False, Top);
    if PacketFilterIsOurs() and PacketFilterInstalled() then
    begin
      PacketBox := TNewCheckBox(AddLine(Form, 'Also remove Windows Packet Filter', True, Top));
      PacketBox.Checked := RemovingPacketFilter;
    end;
    if EqualizerApoInstalled() then
    begin
      ApoBox := TNewCheckBox(AddLine(Form, 'Also remove Equalizer APO', True, Top));
      ApoBox.Checked := RemovingEqualizerApo;
      AddLine(Form, 'Sound may need a restart of Windows before it is as it was.', False, Top);
    end;
    Form.ClientHeight := Top + ScaleY(4 + 23 + 12);
    Next := TNewButton.Create(Form);
    Next.Parent := Form;
    Next.Caption := SetupMessage(msgButtonNext);
    Next.ModalResult := mrOk;
    Next.Default := True;
    Cancel := TNewButton.Create(Form);
    Cancel.Parent := Form;
    Cancel.Caption := SetupMessage(msgButtonCancel);
    Cancel.ModalResult := mrCancel;
    Cancel.Cancel := True;
    Next.Width := Form.CalculateButtonWidth([Next.Caption, Cancel.Caption]);
    Cancel.Width := Next.Width;
    Next.Height := ScaleY(23);
    Cancel.Height := ScaleY(23);
    Next.Top := Form.ClientHeight - ScaleY(23 + 12);
    Cancel.Top := Next.Top;
    Cancel.Left := Form.ClientWidth - ScaleX(12) - Cancel.Width;
    Next.Left := Cancel.Left - ScaleX(6) - Next.Width;
    Form.ActiveControl := Next;
    Result := Form.ShowModal() = mrOk;
    if PacketBox <> nil then RemovingPacketFilter := PacketBox.Checked;
    if ApoBox <> nil then RemovingEqualizerApo := ApoBox.Checked;
  finally
    Form.Free();
  end;
end;

// A copy that stays keeps its record. The sign-in task and the network service go first (the app removed the task already when the
// setting was turned off).
procedure UninstallSteps();
var Driver: Boolean;
begin
  Driver := RemovingPacketFilter and PacketFilterIsOurs();
  if RunsAsAdministrator() then RunApp('--sign-in-task off');
  if NetworkServiceInstalled() then RunApp('--network-service remove');
  if RemovingEqualizerApo and EqualizerApoInstalled() then UninstallEqualizerApo();
  if Driver and PacketFilterInstalled() then RunNetCfg('-v -u ' + PacketFilterComponent);
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
  Result := ChooseWhatToRemove();
  if not Result then Exit;
  Msg := EndOmniControl();
  if Msg <> '' then MsgBox(Msg, mbError, MB_OK);
  Result := Msg = '';
end;
