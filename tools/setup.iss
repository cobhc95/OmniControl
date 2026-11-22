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
; The settings and the "Start with Windows" entry stay the installing user's own (HKCU, %APPDATA%) on purpose.
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
[Messages]
WizardSelectComponents=Choose features
SelectComponentsDesc=Pick what OmniControl should do on this PC. You can change this later in App settings.
SelectComponentsLabel2=Ready-made choice:
; "Choose features": a feature left unticked does not run; "Features you didn't tick" ([Code] LeavesOut) also leaves out its optional files (below),
; so the app shows it as not installed. The component names are the app's feature ids (retyped from OmniControl.App/Features/FeatureList, also in
; [Code] FeatureIds); the choice is written for the app ([Code] WriteFeatures).
[Types]
Name: everything; Description: "Everything"
Name: sound; Description: "Sound only"
Name: monitoring; Description: "Monitoring only"
Name: none; Description: "None"
Name: custom; Description: "Custom"; Flags: iscustom
[Components]
Name: volume; Description: "Sound and effects: volume, mixer, effects (Equalizer APO)"; Types: everything sound
Name: displays; Description: "Displays: brightness, colour, rotation"; Types: everything
Name: input; Description: "Mouse and trackpad: button remapping, gestures"; Types: everything
Name: automation; Description: "Hotkeys and automation: keys, rules, custom actions"; Types: everything
Name: osd; Description: "On-screen display (OSD)"; Types: everything
Name: resources; Description: "Resources and taskbar gadget: CPU, GPU, network, temperatures"; Types: everything monitoring
Name: traffic; Description: "Network traffic control: per-app limits (Windows Packet Filter)"; Types: everything
Name: otherpcs; Description: "Other PCs: pairing, wake, remote control"; Types: everything
Name: devices; Description: "Smart devices: plugs and bulbs"; Types: everything
Name: screenshot; Description: "Screenshot tool"; Types: everything
; The optional files (retyped from Core/Engine/OptionalFiles, the folders from the csproj; the sensor library with what only it loads) and their
; sizes for the disk-space line ([Code] ShowDiskSpace). The publish folder has no other subfolders.
#define SensorFiles "LibreHardwareMonitorLib.dll,DiskInfoToolkit.dll,HidSharp.dll,RAMSPDToolkit-NDD.dll,BlackSharp.Core.dll"
#define public Bytes 0
#define public Handle 0
#define public Found 0
#define public Dir ""
#sub AddFound
  #expr Bytes = Bytes + FileSize(Dir + FindGetFileName(Handle))
#endsub
#sub SumDir
  #expr Bytes = 0
  #for {Handle = Found = FindFirst(Dir + "*", 0); Found; Found = FindNext(Handle)} AddFound
  #expr FindClose(Handle)
#endsub
#expr Dir = PubDir + "\"
#expr SumDir
#define TopBytes Bytes
#expr Dir = PubDir + "\equalizer-apo\"
#expr SumDir
#define VolumeBytes Bytes
#expr Dir = PubDir + "\windows-packet-filter\"
#expr SumDir
#define TrafficBytes Bytes
#define ResourcesBytes FileSize(PubDir + "\LibreHardwareMonitorLib.dll") + FileSize(PubDir + "\DiskInfoToolkit.dll") + FileSize(PubDir + "\HidSharp.dll") + FileSize(PubDir + "\RAMSPDToolkit-NDD.dll") + FileSize(PubDir + "\BlackSharp.Core.dll")
#define ScreenshotBytes FileSize(PubDir + "\zxing.dll")
[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Excludes: "\equalizer-apo,\windows-packet-filter,\zxing.dll,\{#StringChange(SensorFiles, ',', ',\')}"; Flags: recursesubdirs ignoreversion
Source: "{#PubDir}\equalizer-apo\*"; DestDir: "{app}\equalizer-apo"; Flags: ignoreversion; Check: Installs('volume')
Source: "{#PubDir}\windows-packet-filter\*"; DestDir: "{app}\windows-packet-filter"; Flags: ignoreversion; Check: Installs('traffic')
Source: "{#PubDir}\LibreHardwareMonitorLib.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('resources')
Source: "{#PubDir}\DiskInfoToolkit.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('resources')
Source: "{#PubDir}\HidSharp.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('resources')
Source: "{#PubDir}\RAMSPDToolkit-NDD.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('resources')
Source: "{#PubDir}\BlackSharp.Core.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('resources')
Source: "{#PubDir}\zxing.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: Installs('screenshot')
; An upgrade that now leaves a feature out takes away what an earlier Setup put there.
[InstallDelete]
Type: filesandordirs; Name: "{app}\equalizer-apo"; Check: not Installs('volume')
Type: filesandordirs; Name: "{app}\windows-packet-filter"; Check: not Installs('traffic')
Type: files; Name: "{app}\zxing.dll"; Check: not Installs('screenshot')
Type: files; Name: "{app}\LibreHardwareMonitorLib.dll"; Check: not Installs('resources')
Type: files; Name: "{app}\DiskInfoToolkit.dll"; Check: not Installs('resources')
Type: files; Name: "{app}\HidSharp.dll"; Check: not Installs('resources')
Type: files; Name: "{app}\RAMSPDToolkit-NDD.dll"; Check: not Installs('resources')
Type: files; Name: "{app}\BlackSharp.Core.dll"; Check: not Installs('resources')
[Icons]
Name: "{group}\OmniControl"; Filename: "{app}\OmniControl.exe"
Name: "{autodesktop}\OmniControl"; Filename: "{app}\OmniControl.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
; Installs the bundled driver after the files ([Code] InstallSteps); uninstall removes it only if OmniControl's receipt says it installed it.
; Only with Network traffic control, as is the network service.
Name: packetfilter; Description: "Install Windows Packet Filter (for per-app download limits)"; Components: traffic
; Added after the files (the app's --network-service install); left unticked (or without Network traffic control), a service from before is
; removed; uninstall removes it.
Name: networkservice; Description: "Network service (per-app limits without admin)"; Components: traffic
; The only way the sign-in task is added (the app's --sign-in-task on, for the user signed in here); ticked on the Tasks page when that
; user's "Run as administrator" is on ([Code] PresetSignInTask).
Name: signintask; Description: "Start OmniControl as administrator at sign-in"; Flags: unchecked
[Run]
; Other PCs reach OmniControl over Tailscale only through this rule (Core/Engine/Tailscale/TailscaleFirewall: same name, port and ranges), added
; only with Other PCs and when Tailscale is installed (else the app's Tailscale checklist adds it when needed); Setup is elevated, so no later prompt.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OmniControl on Tailscale"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OmniControl on Tailscale"" dir=in action=allow program=""{app}\OmniControl.exe"" protocol=TCP localport=47810 remoteip=100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any"; Flags: runhidden waituntilterminated; Check: TailscaleInstalled; Components: otherpcs
; As the person who started Setup, not elevated: the app's own "Run as administrator" decides.
Filename: "{app}\OmniControl.exe"; Description: "Start OmniControl"; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
; The network blocks a crashed run may have left (OmniControl's own firewall rule group; upload limits end with a restart).
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -Command ""Remove-NetFirewallRule -Group 'OmniControl' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallGroup"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OmniControl on Tailscale"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTailscaleRule"

; ---- Equalizer APO: its official installer (Core/Engine/Apo/ApoInstaller, in {app}\equalizer-apo), run after the files ----
; It is its own program with its own uninstaller and Apps entry; OmniControl's uninstall runs that uninstaller when Setup's receipt says
; Setup installed it and its box stays ticked.
[Tasks]
Name: equalizerapo; Description: "Equalizer APO (master effects on your speakers; skipped if already installed)"; Components: volume
[Code]
// Receipts: what Setup (or the app, for Windows Packet Filter) installed itself, never what was there before, so uninstall offers to remove
// only that. Under HKLM\SOFTWARE, whose access list lets only administrators and SYSTEM change it (the key and Windows Packet Filter's value
// retyped from Core/Engine/Resources/PacketFilterSetup).
const
  ReceiptKey = 'SOFTWARE\OmniControl';
  PacketFilterReceipt = 'PacketFilterInstalled';
  EqualizerApoReceipt = 'EqualizerApoInstalled';

function HasReceipt(Name: String): Boolean;
var Mine: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, ReceiptKey, Name, Mine) and (Mine = 1);
end;

procedure WriteReceipt(Name: String);
begin
  RegWriteDWordValue(HKLM64, ReceiptKey, Name, 1);
end;

procedure DropReceipt(Name: String);
begin
  RegDeleteValue(HKLM64, ReceiptKey, Name);
  RegDeleteKeyIfEmpty(HKLM64, ReceiptKey);
end;

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
  begin
    WriteReceipt(EqualizerApoReceipt);
    EqualizerApoNote := 'Equalizer APO is installed. If its check showed a device that didn''t start, restart Windows; its Device Selector (Start menu) changes its devices.';
  end else
    EqualizerApoNote := 'Equalizer APO was not installed. OmniControl offers it again when you first use a master effect.';
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

// Windows Packet Filter's names, retyped from Core/Engine/Resources/PacketFilterSetup and PacketFilter (Inno's script cannot read C#), and
// the per-user record earlier versions kept instead of the receipt (honoured once: InstallSteps turns it into the receipt).
const
  PacketFilterInf = '{app}\windows-packet-filter\ndisrd_lwf.inf';
  PacketFilterComponent = 'nt_ndisrd';
  PacketFilterRecordKey = 'Software\OmniControl';
  PacketFilterRecordValue = 'PacketFilterInstalled';

// The move from a per-user install: its own uninstaller runs silently (the settings in %APPDATA%\OmniControl stay) with the driver's record
// set aside, so it leaves Windows Packet Filter installed, then its folder goes. Only the usual folder is deleted, never a folder the person
// chose, and only the uninstaller in that folder is run, and only with the token of the person who started Setup, never elevated: that
// folder and the per-user Uninstall entry are the person's own, so anything running as them could have replaced it. The sign-in task (when
// ticked), the network service and the start-up entry are pointed at this install after the files (InstallSteps).
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
// its sign-in task (Run as administrator without a prompt; --sign-in-task on|keep|off, retyped from the app's Shared/Services/AdministratorMode)
// and its network service (--network-service install "<user>"|remove, for this Windows user, named so an administrator's prompt still serves
// them; the switch is retyped from the app's Shared/Services/NetworkServiceMode). The app owns their names and definitions and refuses a folder
// others may change.
procedure RunApp(Args: String);
var Code: Integer;
begin
  Exec(ExpandConstant('{app}\OmniControl.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// The saved "Run as administrator" (Mouse and Trackpad, mouse.json's RunAsAdministrator, retyped from the app's settings) of the person who
// started Setup, also when another account answered Windows' prompt: read as that person, not elevated, by Windows' PowerShell, which reads
// the file as JSON. Off for a new install, its default, and when it cannot be read.
function OriginalUserRunsAsAdministrator(): Boolean;
var Code: Integer;
begin
  Result := ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "try { if ((Get-Content -LiteralPath (Join-Path $env:APPDATA ''OmniControl\mouse.json'') -Raw '
    + '| ConvertFrom-Json).RunAsAdministrator -eq $true) { exit 10 } } catch { exit 12 }; exit 11"', '', SW_HIDE, ewWaitUntilTerminated, Code)
    and (Code = 10);
end;

// The Tasks page ticks the sign-in task by that setting the first time it shows (also over a choice an earlier Setup remembered).
var SignInTaskPreset: Boolean;

procedure PresetSignInTask();
begin
  if SignInTaskPreset then Exit;
  SignInTaskPreset := True;
  if OriginalUserRunsAsAdministrator() then WizardSelectTasks('signintask') else WizardSelectTasks('!signintask');
end;

// Ticked: added (or replaced) for the user signed in here. Unticked on the Tasks page: removed. A silent Setup without the tick keeps a task
// from before only when it is exactly the one Setup makes (this install, the user signed in here, OmniControl's definition), else removes it.
procedure SignInTaskSteps();
begin
  if WizardIsTaskSelected('signintask') then RunApp('--sign-in-task on')
  else if WizardSilent() then RunApp('--sign-in-task keep')
  else RunApp('--sign-in-task off');
end;

// Tailscale's Windows service (its name retyped from Core/Engine/Tailscale/TailscaleAdapter).
function TailscaleInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\Tailscale');
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

// The per-user record of earlier versions becomes the receipt once (only while the driver is there), then goes.
procedure MovePacketFilterRecord();
var Mine: Cardinal;
begin
  if not RegQueryDWordValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue, Mine) then Exit;
  if (Mine = 1) and PacketFilterInstalled() then WriteReceipt(PacketFilterReceipt);
  RegDeleteValue(HKCU, PacketFilterRecordKey, PacketFilterRecordValue);
  RegDeleteKeyIfEmpty(HKCU, PacketFilterRecordKey);
end;

// "Choose features": the components are the app's feature ids, in the order of its FeatureList (retyped, also as [Components] names). The ids
// left unticked are written beside the receipts as FeaturesOff with the time as FeaturesWritten (Core/Engine/SetupFeatures reads both), which
// every user may read; the app takes each run's choice into its own settings once, and App settings owns it afterwards.
const
  FeatureIds = 'volume,displays,input,automation,osd,resources,traffic,otherpcs,devices,screenshot';
  FeaturesValue = 'FeaturesOff';
  FeaturesWrittenValue = 'FeaturesWritten';

// The person who started Setup chose these in App settings (settings.json's FeaturesOff, read as that person by Windows' PowerShell and
// handed back as a bit per feature in the exit code); False when no choice is saved there or it cannot be read.
function OriginalUserFeaturesOff(var Mask: Integer): Boolean;
var Code: Integer;
    Quoted: String;
begin
  Quoted := FeatureIds;
  StringChangeEx(Quoted, ',', ''',''', True);
  Result := ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "try { $o = (Get-Content -LiteralPath (Join-Path $env:APPDATA ''OmniControl\settings.json'') -Raw '
    + '| ConvertFrom-Json).FeaturesOff; if ($o -eq $null) { exit 2 }; $m = 0; $i = 0; foreach ($f in ''' + Quoted + ''') '
    + '{ if ($o -contains $f) { $m = $m -bor (1 -shl $i) }; $i++ }; exit (4096 + $m) } catch { exit 3 }"', '', SW_HIDE, ewWaitUntilTerminated, Code)
    and (Code >= 4096);
  Mask := Code - 4096;
end;

// Inno's own tick-by-tick sync does not run for WizardSelectComponents, so the Ready-made choice is set here to the type whose features are
// exactly the ticked ones (the sets retyped from [Types]), else Custom (the last one).
procedure SyncTypeChoice();
var Ids: TArrayOfString;
    Sets: array of String;
    I, T: Integer;
    Match: Boolean;
begin
  Ids := StringSplit(FeatureIds, [','], stAll);
  SetArrayLength(Sets, 4);
  Sets[0] := FeatureIds;
  Sets[1] := 'volume';
  Sets[2] := 'resources';
  Sets[3] := '';
  for T := 0 to 3 do
  begin
    Match := True;
    for I := 0 to GetArrayLength(Ids) - 1 do
      if WizardIsComponentSelected(Ids[I]) <> (Pos(',' + Ids[I] + ',', ',' + Sets[T] + ',') > 0) then Match := False;
    if Match then
    begin
      WizardForm.TypesCombo.ItemIndex := T;
      Exit;
    end;
  end;
  WizardForm.TypesCombo.ItemIndex := WizardForm.TypesCombo.Items.Count - 1;
end;
// An upgrade shows the features as they are now: the choice in App settings, else the one an earlier Setup wrote, else Inno's own (its
// previous components, or Everything).
var FeaturesPreset: Boolean;

procedure PresetFeatures();
var Ids: TArrayOfString;
    Mask, I: Integer;
    Known: Boolean;
    Off, Pick: String;
begin
  if FeaturesPreset then Exit;
  FeaturesPreset := True;
  Ids := StringSplit(FeatureIds, [','], stAll);
  Known := OriginalUserFeaturesOff(Mask);
  if not Known and RegQueryStringValue(HKLM64, ReceiptKey, FeaturesValue, Off) then
  begin
    Known := True;
    Mask := 0;
    for I := 0 to GetArrayLength(Ids) - 1 do
      if Pos(',' + Ids[I] + ',', ',' + Off + ',') > 0 then Mask := Mask or (1 shl I);
  end;
  if not Known then Exit;
  Pick := '';
  for I := 0 to GetArrayLength(Ids) - 1 do
  begin
    if Pick <> '' then Pick := Pick + ',';
    if (Mask and (1 shl I)) <> 0 then Pick := Pick + '!';
    Pick := Pick + Ids[I];
  end;
  WizardSelectComponents(Pick);
end;

// "Features you didn't tick" under the list: Don't install them, or Install them but keep them off (the default when no choice is kept). The choice is kept beside
// FeaturesOff, so an upgrade shows it again and a silent Setup follows it; a silent Setup with none kept installs everything, as before.
const
  LeaveOutValue = 'LeaveOutUnticked';

var LeaveOutButton, KeepOffButton: TNewRadioButton;
    LeaveOutNote, KeepOffNote, LeftOutText: TNewStaticText;
    OldClickCheck, OldTypeChange: TNotifyEvent;

function LeavesOut(): Boolean;
var Kept: Cardinal;
begin
  if LeaveOutButton <> nil then Result := LeaveOutButton.Checked
  else Result := RegQueryDWordValue(HKLM64, ReceiptKey, LeaveOutValue, Kept) and (Kept = 1);
end;

function Installs(Id: String): Boolean;
begin
  Result := WizardIsComponentSelected(Id) or not LeavesOut();
end;

// What an unticked feature leaves out of the install (the one place for this map); a feature with no separate piece says nothing.
function LeftOutPiece(Id: String): String;
begin
  Result := '';
  if Id = 'volume' then Result := 'Equalizer APO (Sound)'
  else if Id = 'resources' then Result := 'sensor libraries (Resources)'
  else if Id = 'traffic' then Result := 'Windows Packet Filter and network service (Network traffic control)'
  else if Id = 'otherpcs' then Result := 'Tailscale firewall rule (Other PCs)'
  else if Id = 'screenshot' then Result := 'QR-code library (Screenshot tool)';
end;

// The note under Don't install them: shown only while that choice is picked, its space always kept.
procedure ShowLeftOut();
var Ids: TArrayOfString;
    I: Integer;
    List, Piece: String;
begin
  if LeftOutText = nil then Exit;
  Ids := StringSplit(FeatureIds, [','], stAll);
  for I := 0 to GetArrayLength(Ids) - 1 do
  begin
    Piece := LeftOutPiece(Ids[I]);
    if (Piece <> '') and not WizardIsComponentSelected(Ids[I]) then
    begin
      if List <> '' then List := List + ', ';
      List := List + Piece;
    end;
  end;
  if List = '' then List := 'nothing';
  LeftOutText.Caption := 'Left out: ' + List + '. Still installed, not running: the rest of these features.';
  LeftOutText.Visible := LeaveOutButton.Checked;
end;

// Inno counts every file whatever is ticked (the optional ones go by Check), so the line is said here: all files less those left out.
procedure ShowDiskSpace();
var Bytes: Extended;
    Text: String;
begin
  Bytes := {#TopBytes} + {#VolumeBytes} + {#TrafficBytes};
  if not Installs('volume') then Bytes := Bytes - {#VolumeBytes};
  if not Installs('traffic') then Bytes := Bytes - {#TrafficBytes};
  if not Installs('resources') then Bytes := Bytes - {#ResourcesBytes};
  if not Installs('screenshot') then Bytes := Bytes - {#ScreenshotBytes};
  Text := SetupMessage(msgComponentsDiskSpaceMBLabel);
  StringChangeEx(Text, '[mb]', Format('%.1f', [Bytes / 1048576]), True);
  WizardForm.ComponentsDiskSpaceLabel.Caption := Text;
  ShowLeftOut();
end;

procedure ChoiceClicked(Sender: TObject);
begin
  ShowDiskSpace();
end;

procedure ListClicked(Sender: TObject);
begin
  OldClickCheck(Sender);
  ShowDiskSpace();
end;

procedure TypeChanged(Sender: TObject);
begin
  OldTypeChange(Sender);
  ShowDiskSpace();
end;

// A muted note after a radio button's words; clicking it picks the button.
procedure NoteClicked(Sender: TObject);
begin
  LeaveOutButton.Checked := Sender = LeaveOutNote;
  KeepOffButton.Checked := Sender = KeepOffNote;
  ShowDiskSpace();
end;

function AddChoice(Box: TPanel; Text, Note: String; Top: Integer; var Muted: TNewStaticText): TNewRadioButton;
var Words: TNewStaticText;
begin
  Words := TNewStaticText.Create(Box);
  Words.Parent := Box;
  Words.Caption := Text;
  Result := TNewRadioButton.Create(Box);
  Result.Parent := Box;
  Result.Caption := Text;
  Result.Left := ScaleX(8);
  Result.Top := Top;
  Result.Width := Words.Width + ScaleX(20);
  Result.Height := ScaleY(17);
  Result.OnClick := @ChoiceClicked;
  Words.Free();
  Muted := TNewStaticText.Create(Box);
  Muted.Parent := Box;
  Muted.Caption := Note;
  Muted.Font.Color := clGrayText;
  Muted.Left := Result.Left + Result.Width + ScaleX(2);
  Muted.Top := Top + ScaleY(1);
  Muted.OnClick := @NoteClicked;
end;

var ChoiceBox: TPanel;

// The box and the disk-space line hold the bottom of the page by anchors and the list gives up what is left above them; run again when the
// page shows because Inno sizes its controls after InitializeWizard.
procedure LayoutChoice();
var List: TNewCheckListBox;
    Gap: Integer;
begin
  if ChoiceBox = nil then Exit;
  List := WizardForm.ComponentsList;
  Gap := ScaleY(8);
  List.Anchors := [akLeft, akTop, akRight];
  WizardForm.ComponentsDiskSpaceLabel.Anchors := [akLeft, akRight, akBottom];
  ChoiceBox.Anchors := [akLeft, akRight, akBottom];
  ChoiceBox.Left := List.Left;
  ChoiceBox.Width := List.Width;
  ChoiceBox.Top := WizardForm.ComponentsDiskSpaceLabel.Top - Gap - ChoiceBox.Height;
  List.Height := ChoiceBox.Top - Gap - List.Top;
  List.Anchors := [akLeft, akTop, akRight, akBottom];
  if not LeaveOutButton.Checked and not KeepOffButton.Checked then KeepOffButton.Checked := True;
end;

procedure InitializeWizard();
var Title: TNewStaticText;
    Kept: Cardinal;
    NoteHeight: Integer;
begin
  if WizardSilent() then Exit;
  ChoiceBox := TPanel.Create(WizardForm);
  ChoiceBox.Parent := WizardForm.ComponentsList.Parent;
  ChoiceBox.BevelOuter := bvNone;
  ChoiceBox.BorderStyle := bsSingle;
  ChoiceBox.ParentBackground := False;
  ChoiceBox.Color := clWindow;
  NoteHeight := ScaleY(44);
  ChoiceBox.Height := ScaleY(66) + NoteHeight;
  Title := TNewStaticText.Create(ChoiceBox);
  Title.Parent := ChoiceBox;
  Title.Caption := 'Features you didn''t tick:';
  Title.Left := ScaleX(8);
  Title.Top := ScaleY(6);
  LeaveOutButton := AddChoice(ChoiceBox, 'Don''t install them', '(their drivers, services and sensor library are left out)', ScaleY(24), LeaveOutNote);
  KeepOffButton := AddChoice(ChoiceBox, 'Install them but keep them off', '(turn on any time in App settings)', ScaleY(42) + NoteHeight, KeepOffNote);
  LeftOutText := TNewStaticText.Create(ChoiceBox);
  LeftOutText.Parent := ChoiceBox;
  LeftOutText.AutoSize := False;
  LeftOutText.WordWrap := True;
  LeftOutText.Font.Color := clGrayText;
  LeftOutText.Left := ScaleX(28);
  LeftOutText.Top := ScaleY(42);
  LeftOutText.Width := WizardForm.ComponentsList.Width - ScaleX(28) - ScaleX(12);
  LeftOutText.Height := NoteHeight;
  LeftOutText.Anchors := [akLeft, akTop, akRight];
  LeaveOutButton.Checked := RegQueryDWordValue(HKLM64, ReceiptKey, LeaveOutValue, Kept) and (Kept = 1);
  KeepOffButton.Checked := not LeaveOutButton.Checked;
  ShowLeftOut();
  LayoutChoice();
  OldClickCheck := WizardForm.ComponentsList.OnClickCheck;
  WizardForm.ComponentsList.OnClickCheck := @ListClicked;
  OldTypeChange := WizardForm.TypesCombo.OnChange;
  WizardForm.TypesCombo.OnChange := @TypeChanged;
end;
// A silent Setup changes the app's choice only when its command line names one (/COMPONENTS or /TYPE).
function ChoiceGiven(): Boolean;
var I: Integer;
begin
  Result := not WizardSilent();
  for I := 1 to ParamCount do
    if (CompareText(Copy(ParamStr(I), 1, 12), '/COMPONENTS=') = 0) or (CompareText(Copy(ParamStr(I), 1, 6), '/TYPE=') = 0) then Result := True;
end;

procedure WriteFeatures();
var Ids: TArrayOfString;
    I: Integer;
    Off: String;
begin
  if not ChoiceGiven() then Exit;
  Ids := StringSplit(FeatureIds, [','], stAll);
  Off := '';
  for I := 0 to GetArrayLength(Ids) - 1 do
    if not WizardIsComponentSelected(Ids[I]) then
    begin
      if Off <> '' then Off := Off + ',';
      Off := Off + Ids[I];
    end;
  RegWriteStringValue(HKLM64, ReceiptKey, FeaturesValue, Off);
  RegWriteStringValue(HKLM64, ReceiptKey, FeaturesWrittenValue, GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':'));
  if not WizardSilent() then RegWriteDWordValue(HKLM64, ReceiptKey, LeaveOutValue, Ord(LeavesOut()));
end;

// A driver install that fails leaves OmniControl installed and says so in one line. The receipt lets uninstall remove only this copy.
procedure InstallSteps();
var Driver: Boolean;
begin
  WriteFeatures();
  PointStartupEntry();
  MovePacketFilterRecord();
  Driver := WizardIsTaskSelected('packetfilter') and not PacketFilterInstalled();
  if Driver then RunNetCfg('-v -l "' + ExpandConstant(PacketFilterInf) + '" -c s -i ' + PacketFilterComponent);
  SignInTaskSteps();
  if WizardIsTaskSelected('networkservice') then
    RunApp('--network-service install "' + GetEnv('USERDOMAIN') + '\' + GetUserNameString() + '"')
  else if NetworkServiceInstalled() then
    RunApp('--network-service remove');
  InstallEqualizerApo();
  if not Driver then Exit;
  if PacketFilterInstalled() then
    WriteReceipt(PacketFilterReceipt)
  else
    SuppressibleMsgBox('Windows Packet Filter was not installed. OmniControl installs it when you first set a download limit.', mbInformation, MB_OK, IDOK);
end;

// Only what the receipts say OmniControl installed, and is still there, is offered for removal, so another program's is never taken away.
function PacketFilterIsOurs(): Boolean;
begin
  Result := HasReceipt(PacketFilterReceipt) and PacketFilterInstalled();
end;

function EqualizerApoIsOurs(): Boolean;
begin
  Result := HasReceipt(EqualizerApoReceipt) and EqualizerApoInstalled();
end;

// The uninstall choices for the parts with a receipt; both on unless /KEEPDRIVERS is passed, also for a silent uninstall, which shows no page.
var RemovingPacketFilter, RemovingEqualizerApo: Boolean;

function KeepsDrivers(): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/KEEPDRIVERS') = 0 then Result := True;
end;

function AddBox(Page: TNewNotebookPage; Text: String; var Top: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(Page);
  Result.Parent := Page;
  Result.Caption := Text;
  Result.Checked := not KeepsDrivers();
  Result.Left := 0;
  Result.Top := Top;
  Result.Width := Page.Width;
  Result.Height := ScaleY(17);
  Top := Top + Result.Height + ScaleY(8);
end;

// Inno's uninstaller has no Tasks page, so its own window shows one first, before anything is removed: a box for each part that is there and
// has a receipt (no page when none has), and Uninstall or Cancel.
procedure InitializeUninstallProgressForm();
var Page: TNewNotebookPage;
    Before: TNewNotebookPage;
    PacketBox, ApoBox: TNewCheckBox;
    Note: TNewStaticText;
    Go: TNewButton;
    Top: Integer;
    Name, Description: String;
begin
  RemovingPacketFilter := not KeepsDrivers();
  RemovingEqualizerApo := RemovingPacketFilter;
  if UninstallSilent() or not (PacketFilterIsOurs() or EqualizerApoIsOurs()) then Exit;
  Page := TNewNotebookPage.Create(UninstallProgressForm);
  Page.Notebook := UninstallProgressForm.InnerNotebook;
  Page.Parent := UninstallProgressForm.InnerNotebook;
  Page.Align := alClient;
  Top := 0;
  PacketBox := nil;
  ApoBox := nil;
  if PacketFilterIsOurs() then PacketBox := AddBox(Page, 'Remove Windows Packet Filter', Top);
  if EqualizerApoIsOurs() then
  begin
    ApoBox := AddBox(Page, 'Remove Equalizer APO', Top);
    Note := TNewStaticText.Create(Page);
    Note.Parent := Page;
    Note.Caption := 'Sound may need a restart of Windows before it is as it was.';
    Note.Left := ScaleX(17);
    Note.Top := Top - ScaleY(6);
  end;
  Before := UninstallProgressForm.InnerNotebook.ActivePage;
  Name := UninstallProgressForm.PageNameLabel.Caption;
  Description := UninstallProgressForm.PageDescriptionLabel.Caption;
  UninstallProgressForm.InnerNotebook.ActivePage := Page;
  UninstallProgressForm.PageNameLabel.Caption := 'What to remove';
  UninstallProgressForm.PageDescriptionLabel.Caption := 'Untick anything you want to keep on this PC.';
  Go := TNewButton.Create(UninstallProgressForm);
  Go.Parent := UninstallProgressForm;
  Go.Caption := 'Uninstall';
  Go.Width := UninstallProgressForm.CancelButton.Width;
  Go.Height := UninstallProgressForm.CancelButton.Height;
  Go.Top := UninstallProgressForm.CancelButton.Top;
  Go.Left := UninstallProgressForm.CancelButton.Left - Go.Width - ScaleX(6);
  Go.ModalResult := mrOk;
  Go.Default := True;
  UninstallProgressForm.CancelButton.Enabled := True;
  UninstallProgressForm.CancelButton.ModalResult := mrCancel;
  UninstallProgressForm.ActiveControl := Go;
  if UninstallProgressForm.ShowModal() = mrCancel then Abort;
  if PacketBox <> nil then RemovingPacketFilter := PacketBox.Checked;
  if ApoBox <> nil then RemovingEqualizerApo := ApoBox.Checked;
  Go.Visible := False;
  UninstallProgressForm.CancelButton.Enabled := False;
  UninstallProgressForm.InnerNotebook.ActivePage := Before;
  UninstallProgressForm.PageNameLabel.Caption := Name;
  UninstallProgressForm.PageDescriptionLabel.Caption := Description;
end;
// The sign-in task and the network service go first. A part that stays keeps its receipt, so a later uninstall still offers it; a part
// that is gone takes its receipt with it.
procedure UninstallSteps();
var Driver: Boolean;
begin
  Driver := RemovingPacketFilter and PacketFilterIsOurs();
  RunApp('--sign-in-task off');
  if NetworkServiceInstalled() then RunApp('--network-service remove');
  if RemovingEqualizerApo and EqualizerApoIsOurs() then UninstallEqualizerApo();
  if Driver then RunNetCfg('-v -u ' + PacketFilterComponent);
  if Driver and PacketFilterInstalled() then
    SuppressibleMsgBox('Windows Packet Filter was not removed. Installing and uninstalling OmniControl again removes it.', mbInformation, MB_OK, IDOK);
  if not PacketFilterInstalled() then DropReceipt(PacketFilterReceipt);
  if not EqualizerApoInstalled() then DropReceipt(EqualizerApoReceipt);
  DropReceipt(FeaturesValue);
  DropReceipt(FeaturesWrittenValue);
  DropReceipt(LeaveOutValue);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectComponents) and not WizardSilent() then
  begin
    PresetFeatures();
    ShowDiskSpace();
  end;
  if (CurPageID = wpSelectTasks) and not WizardSilent() then PresetSignInTask();
  if (CurPageID = wpFinished) and (EqualizerApoNote <> '') then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + EqualizerApoNote;
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
