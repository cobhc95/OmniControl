namespace OmniControl.Core.Engine.Apo;

/// How a step of OmniControl's own Equalizer APO ended (the elevated helper's exit code).
public enum ApoOutcome
{
    /// Done; a device that was attached is checked and running.
    Done = 0,
    Failed = 1,
    /// Equalizer APO did not start on the device in any install mode tried; as in Device Selector it stays on it in the mode that did best.
    NotStarted = 2,
    /// Done in the registry, but Windows audio did not restart: it takes effect after the next restart.
    NeedsRestart = 3,
    /// A separately installed Equalizer APO is here: nothing was installed or changed (only OmniControl's own leftovers were taken away).
    Separate = 4,
    /// OmniControl's copy is not installed, so there was nothing to change.
    NotOurs = 5,
    /// The administrator prompt was refused, or Windows blocked or did not start the helper.
    NotRun = 6,
    /// The device has no effect slots in Windows (Remote Audio and other remote devices), so it was left alone; an install before it still happened.
    Unsupported = 7,
    /// Equalizer APO started on the device, but the driver's own APO did not start inside it.
    OriginalNotStarted = 8,
    /// Done, but the device is disabled or unplugged, so it could not be checked (Device Selector skips it too).
    NotChecked = 9,
    /// Nothing differed from what is in place, so nothing was changed (Device Selector's OK stays greyed).
    Unchanged = 10,
    /// The device was ticked with neither APO to install, so Device Selector's install wrote nothing and it is without Equalizer APO.
    NothingInstalled = 11,
}

/// OmniControl's own Equalizer APO: install, attach a device, detach one, remove all, take away leftovers. Every step needs administrator
/// rights, so the app runs itself elevated as the helper (<c>OmniControl.exe --equalizer-apo ...</c>, also Setup's install and uninstall)
/// through <see cref="Run"/>; the helper's side is <see cref="FromCommandLine"/>. A separately installed Equalizer APO is never changed.
/// The steps are Equalizer APO's own: its installer (Setup.nsi) for install and remove, its Device Selector's OK (onDialogAccepted) for a
/// device's tick and options, and after every change its check (<see cref="ApoDeviceTest"/>: Windows audio restarts, every device it is on
/// is checked and, with "Auto" on, tried in the other install modes). Install keeps what was there before in its marker, so removing puts it back.
public static class ApoSetup
{
    public const string Argument = "--equalizer-apo";
    private const string InstallVerb = "install", AttachVerb = "attach", DetachVerb = "detach", RemoveVerb = "remove", CleanVerb = "clean";

    /// A device's last check, kept in the marker's Devices key for every device Equalizer APO is on (the Volume page shows it): not checked
    /// (disabled or unplugged), running, running without the driver's own APO inside it, did not start, or waiting for an audio restart.
    public const string Attached = "attached", Running = "running", OriginalFailed = "original", Failed = "failed", NeedsRestart = "restart";

    private const string LastResultValue = "Last result", ConfigPathValue = "ConfigPath", EnableTraceValue = "EnableTrace";

    private static readonly IApoRegistry Reg = new MachineRegistry();

    /// The arguments of each step (the app's side).
    public static string Install(string? attachGuid = null) => $"{Argument} {InstallVerb} {attachGuid}".TrimEnd();
    /// No state: as Device Selector would put it on that device.
    public static string Attach(string guid, ApoInstallState? state = null) => $"{Argument} {AttachVerb} {guid} {(state is null ? null : Encode(state))}".TrimEnd();
    public static string Detach(string guid) => $"{Argument} {DetachVerb} {guid}";
    public static string Remove() => $"{Argument} {RemoveVerb}";
    public static string Clean() => $"{Argument} {CleanVerb}";

    /// Runs a step as administrator (Windows asks unless OmniControl already is) and waits. Call off the UI thread.
    public static ApoOutcome Run(string arguments)
    {
        var result = HelperProcess.RunElevated(Environment.ProcessPath ?? string.Empty, arguments, hidden: true);
        return result.Failure != HelperFailure.None ? ApoOutcome.NotRun
            : Enum.IsDefined(typeof(ApoOutcome), result.ExitCode) ? (ApoOutcome)result.ExitCode : ApoOutcome.Failed;
    }

    /// What the last step wrote about a failure (for "Copy details"), or null.
    public static string? LastResult() => Reg.ReadString(ApoIds.OwnMarkerKey, LastResultValue);

    /// The helper's side: the exit code of the step the arguments ask for, or null when they ask something else.
    public static int? FromCommandLine(string[] args)
    {
        var at = Array.IndexOf(args, Argument);
        if (at < 0 || at + 1 >= args.Length) return null;
        string? Arg(int i) => at + i < args.Length ? args[at + i] : null;
        try
        {
            Reg.DeleteValue(ApoIds.OwnMarkerKey, LastResultValue);
            return (int)(Arg(1) switch
            {
                InstallVerb => InstallOurs(Arg(2)),
                AttachVerb when Arg(2) is { } guid && (Arg(3) is null || Decode(Arg(3)) is not null) => AttachDevice(guid, Decode(Arg(3))),
                DetachVerb when Arg(2) is { } guid => DetachDevice(guid),
                RemoveVerb => RemoveOurs(),
                CleanVerb => CleanUp(ApoStatus.Ownership().Cleanup) ? ApoOutcome.Done : ApoOutcome.Failed,
                _ => ApoOutcome.Failed,
            });
        }
        catch (Exception ex) when (MachineRegistry.IsRegistryFailure(ex) || ex is InvalidDataException or Win32Exception)
        {
            return (int)Fail($"{string.Join(' ', args[(at + 1)..])}: {ex.Message}"); // the step, then the call and key that stopped it
        }
    }

    private static ApoOutcome Fail(string why)
    {
        try
        {
            Reg.WriteString(ApoIds.OwnMarkerKey, LastResultValue, why);
        }
        catch (Exception ex) when (MachineRegistry.IsRegistryFailure(ex))
        {
            // the outcome code still says it failed
        }
        return ApoOutcome.Failed;
    }

    /// Equalizer APO's installer (Setup.nsi "-Install"), then its "DeviceSelector.exe /i": the files, Users' full access to the config
    /// folder, ConfigPath and EnableTrace, the protected audio path value, regsvr32, then the device (when one is named) and the check.
    private static ApoOutcome InstallOurs(string? attachGuid)
    {
        var owner = ApoStatus.Ownership();
        if (owner.Owner == ApoOwner.Separate)
        {
            CleanUp(owner.Cleanup);
            return ApoOutcome.Separate;
        }
        if (owner.Owner == ApoOwner.None)
        {
            CleanUp(owner.Cleanup);
            if (!ApoFiles.Bundled) return Fail("This build of OmniControl does not include Equalizer APO.");
        }
        if (ApoFiles.Place(out var changed) is { } why) return Fail(why);
        if (owner.Owner == ApoOwner.None)
        {
            Reg.WriteString(ApoIds.OwnMarkerKey, ApoIds.OwnPathValue, ApoFiles.InstallFolder);
            Keep(ApoIds.UpstreamAppKey, ConfigPathValue);
            Keep(ApoIds.UpstreamAppKey, EnableTraceValue);
            Keep(ApoIds.AudioKey, ApoIds.ProtectedAudioValue);
            Reg.WriteString(ApoIds.UpstreamAppKey, ConfigPathValue, ApoFiles.ConfigFolder);
        }
        if (string.IsNullOrEmpty(Reg.ReadString(ApoIds.UpstreamAppKey, EnableTraceValue))) Reg.WriteString(ApoIds.UpstreamAppKey, EnableTraceValue, "false");
        WriteProtectedAudioValue();
        var code = RunTool("regsvr32.exe", $"/s \"{ApoFiles.Dll}\"");
        if (ApoStatus.Ownership().Owner != ApoOwner.Ours) return Fail($"Registering Equalizer APO failed (regsvr32 ended with code {code}).");
        // Only a real install or update is checked: OmniControl's Setup runs this step on every OmniControl update.
        return attachGuid is not null ? AttachDevice(attachGuid, null, check: true)
            : owner.Owner == ApoOwner.None || changed ? Check(null) : ApoOutcome.Unchanged;
    }

    private static void WriteProtectedAudioValue()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var audio = machine.CreateSubKey(ApoIds.AudioKey, writable: true);
        audio.SetValue(ApoIds.ProtectedAudioValue, 1, RegistryValueKind.DWord);
    }

    /// Device Selector's checkProtectedAudioDG(fix) and checkAPORegistration(fix), done each time it opens; true when something had to be
    /// fixed (Device Selector then runs its check even without a device change).
    private static bool FixWindows()
    {
        var fixedAny = false;
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var audio = machine.OpenSubKey(ApoIds.AudioKey))
            if (audio?.GetValue(ApoIds.ProtectedAudioValue) is not int v || v != 1)
            {
                WriteProtectedAudioValue();
                fixedAny = true;
            }
        if (!ApoStatus.Registered())
        {
            RunTool("regsvr32.exe", $"/s \"{ApoFiles.Dll}\"");
            fixedAny = true;
        }
        return fixedAny;
    }

    /// One device ticked, with these options (Device Selector's OK for that row): installed when it is not, installed again when it is but
    /// the options differ, its record is from an older Device Selector or its enhancements are off, else left as it is; then the check.
    /// <param name="check">Check even when nothing changed (its installer's "/i").</param>
    private static ApoOutcome AttachDevice(string guid, ApoInstallState? wanted, bool check = false)
    {
        if (ApoStatus.Ownership().Owner != ApoOwner.Ours) return ApoOutcome.NotOurs;
        if (DeviceApo.KeyOf(Reg, guid) is null) return ApoOutcome.Unsupported;
        check |= FixWindows();
        var device = DeviceApo.Load(Reg, guid) ?? throw new InvalidDataException("The device is not plugged in.");
        var state = wanted ?? device.Current;
        if (!device.Installed) device.Install(Reg, state, ApoFiles.SaveBackup);
        else if (device.CanBeUpgraded || state != device.Current || device.EnhancementsOff) device.Reinstall(Reg, state, ApoFiles.SaveBackup);
        else if (!check) return ApoOutcome.Unchanged;
        return Check(guid);
    }

    /// One device unticked (Device Selector's OK for that row): its own slots come back, then the check of the devices still on.
    private static ApoOutcome DetachDevice(string guid)
    {
        if (ApoStatus.Ownership().Owner != ApoOwner.Ours) return ApoOutcome.NotOurs;
        var check = FixWindows();
        if (DeviceApo.Load(Reg, guid) is { Installed: true } device) device.Uninstall(Reg);
        else if (!check) return ApoOutcome.Unchanged;
        return Check(null);
    }

    /// Device Selector's finish: the check of every device Equalizer APO is on (<see cref="ApoDeviceTest"/>), each one's result kept in the
    /// marker; the outcome for <paramref name="guid"/> (or for the whole check when null).
    private static ApoOutcome Check(string? guid)
    {
        var result = ApoDeviceTest.Run(Reg, ApoFiles.SaveBackup);
        var on = DeviceApo.LoadAll(Reg, input: false).Concat(DeviceApo.LoadAll(Reg, input: true)).Where(d => d.Installed).Select(d => d.Guid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gone in OwnDevices().Where(g => !on.Contains(g))) Reg.DeleteValue(ApoIds.OwnDevicesKey, gone);
        foreach (var device in on)
            Reg.WriteString(ApoIds.OwnDevicesKey, device, result.Checks.GetValueOrDefault(device) ?? (result.RestartFailed is null ? Attached : NeedsRestart));
        if (result.RestartFailed is { } why)
        {
            Fail(why);
            return ApoOutcome.NeedsRestart;
        }
        if (guid is null) return ApoOutcome.Done;
        if (!on.Contains(guid)) return ApoOutcome.NothingInstalled;
        return result.Checks.GetValueOrDefault(guid) switch
        {
            Running => ApoOutcome.Done,
            OriginalFailed => ApoOutcome.OriginalNotStarted,
            Failed => ApoOutcome.NotStarted,
            _ => ApoOutcome.NotChecked,
        };
    }

    /// Equalizer APO's uninstaller ("DeviceSelector.exe /u" takes it off every device it is on, then regsvr32 /u and the protected audio
    /// path value); the values OmniControl's install changed are put back as they were, Windows audio restarts once (so the DLL is let go)
    /// and the files go.
    private static ApoOutcome RemoveOurs()
    {
        var owner = ApoStatus.Ownership();
        if (owner.Owner != ApoOwner.Ours)
        {
            CleanUp(owner.Cleanup);
            return owner.Owner == ApoOwner.Separate ? ApoOutcome.Separate : ApoOutcome.NotOurs;
        }
        var refused = new List<string>();
        foreach (var device in DeviceApo.LoadAll(Reg, input: false).Concat(DeviceApo.LoadAll(Reg, input: true)).Where(d => d.Installed))
        {
            try
            {
                device.Uninstall(Reg);
            }
            catch (Exception ex) when (MachineRegistry.IsRegistryFailure(ex))
            {
                refused.Add(ex.Message); // Device Selector /u shows it and goes on with the next device
            }
        }
        if (refused.Count > 0) Fail(string.Join(" ", refused));
        RunTool("regsvr32.exe", $"/u /s \"{ApoFiles.Dll}\"");
        PutBack(ApoIds.AudioKey, ApoIds.ProtectedAudioValue);
        PutBack(ApoIds.UpstreamAppKey, ConfigPathValue);
        PutBack(ApoIds.UpstreamAppKey, EnableTraceValue);
        if (Reg.KeyExists(ApoIds.UpstreamAppKey) && Reg.KeyEmpty(ApoIds.UpstreamAppKey)) Reg.DeleteKey(ApoIds.UpstreamAppKey);
        var restarted = AudioService.Restart() is null;
        CleanUp(ApoCleanup.FilesAndMarker);
        return refused.Count > 0 ? ApoOutcome.Failed : restarted ? ApoOutcome.Done : ApoOutcome.NeedsRestart;
    }

    /// Only OmniControl's own leftovers: its files (when no other install uses its folder) and its marker.
    private static bool CleanUp(ApoCleanup cleanup)
    {
        if (cleanup == ApoCleanup.FilesAndMarker) ApoFiles.Delete();
        if (cleanup != ApoCleanup.None) DeleteTree(ApoIds.OwnMarkerKey);
        return true;
    }

    private static void DeleteTree(string key)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        machine.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        if (Path.GetDirectoryName(key) is { } parent && machine.OpenSubKey(parent) is { } p)
        {
            var empty = p.SubKeyCount == 0 && p.ValueCount == 0;
            p.Dispose();
            if (empty) machine.DeleteSubKey(parent, throwOnMissingSubKey: false);
        }
    }

    private static IReadOnlyList<string> OwnDevices()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var devices = machine.OpenSubKey(ApoIds.OwnDevicesKey);
        return devices?.GetValueNames() ?? Array.Empty<string>();
    }

    /// Keeps the value as it is before the install changes it ("" = it was not there), once.
    private static void Keep(string key, string name)
    {
        var kept = ApoIds.OwnPreviousPrefix + name;
        if (Reg.ValueExists(ApoIds.OwnMarkerKey, kept)) return;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = machine.OpenSubKey(key);
        Reg.WriteString(ApoIds.OwnMarkerKey, kept, k?.GetValue(name) switch { int d => $"#{d}", string s => s, _ => string.Empty });
    }

    /// Puts back what <see cref="Keep"/> kept.
    private static void PutBack(string key, string name)
    {
        if (Reg.ReadString(ApoIds.OwnMarkerKey, ApoIds.OwnPreviousPrefix + name) is not { } kept) return;
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = machine.OpenSubKey(key, writable: true);
        if (k is null) return;
        if (kept.Length == 0) k.DeleteValue(name, throwOnMissingValue: false);
        else if (kept[0] == '#' && int.TryParse(kept[1..], out var d)) k.SetValue(name, d, RegistryValueKind.DWord);
        else k.SetValue(name, kept);
    }

    private static int RunTool(string exe, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe), arguments) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new Win32Exception($"{exe} did not start.");
        process.WaitForExit(CoreTiming.ElevatedHelperTimeoutMs);
        return process.HasExited ? process.ExitCode : -1;
    }

    /// A state as one argument: the mode's number, then 1 or 0 for pre-mix, post-mix, original pre-mix, original post-mix, silent buffer, auto adjust.
    internal static string Encode(ApoInstallState s) =>
        $"{(int)s.Mode}{B(s.PreMix)}{B(s.PostMix)}{B(s.OriginalPreMix)}{B(s.OriginalPostMix)}{B(s.AllowSilentBuffer)}{B(s.AutoAdjust)}";

    private static char B(bool on) => on ? '1' : '0';

    internal static ApoInstallState? Decode(string? text) =>
        text is { Length: 7 } && text[0] is >= '0' and <= '2' && text.Skip(1).All(c => c is '0' or '1')
            ? new ApoInstallState((ApoInstallMode)(text[0] - '0'), text[1] == '1', text[2] == '1', text[3] == '1', text[4] == '1', text[5] == '1', text[6] == '1')
            : null;
}
