using System.Runtime.InteropServices;

namespace OmniControl.App.Features.Volume.Audio;

/// A program as the mixer sees it right now.
public sealed record RunningApp(string Key, bool IsOpen, bool IsPlaying, int Volume, int Pan, bool Muted);

/// Tracks the programs playing sound, remembers each one's volume, output device and EQ in <see cref="MixerSettings"/>,
/// and re-applies them when a program opens again.
public sealed partial class MixerService : IDisposable
{
    /// The key of the system master in the Volume page and the FX windows (programs use their own key; the master chain itself is addressed by null).
    public const string MasterKey = "master";

    private readonly DispatcherTimer _timer = new() { Interval = Timing.MixerRefreshIdle };
    private readonly HoldCount _closeWatch = new();
    private readonly IHardwareWatches _hardware;
    private readonly Dictionary<string, List<AudioSession>> _sessions = new();
    private readonly DeviceMeters _meters = new(); // the devices' meter objects, kept between reads and released with the mixer
    private readonly Dictionary<int, string?> _paths = new();
    private readonly HashSet<int> _applied = new();
    private readonly Dictionary<string, DateTime> _userChange = new();

    public MixerSettings Settings { get; } = MixerSettings.Load();
    /// Playback devices plugged in and enabled now (virtual cables included).
    public IReadOnlyList<AudioDevice> Devices { get; private set; } = Array.Empty<AudioDevice>();

    /// Recording devices plugged in and enabled now (virtual cables' recording sides included).
    public IReadOnlyList<AudioDevice> RecordingDevices { get; private set; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<RunningApp> Running { get; private set; } = Array.Empty<RunningApp>();

    /// Raised after a scan that found a change (programs opened or closed, devices changed, volumes moved elsewhere), after every scan while
    /// a view watches closely, and when a setting a row shows changed here.
    public event Action? Updated;

    /// Reports problems the user should know about (a device change Windows refused).
    public Action<string>? Report { get; }

    /// What the effects engine holds on programs' streams (silenced, kept on a cable).
    public StreamHolds Holds { get; }

    /// <param name="hardware">The master list of hardware watches: the mixer registers its sound devices there and tells it when Windows announces a change.</param>
    public MixerService(IHardwareWatches hardware, Action<string>? report = null)
    {
        Report = report;
        _hardware = hardware;
        Holds = new StreamHolds(key => _sessions.GetValueOrDefault(key), Find, DirectRouteOf, report);
        _timer.Tick += (_, _) => Scan();
        _closeWatch.Changed += OnCloseWatchChanged;
        ScanAll(); // the first look runs on a worker; the pages and the engine take it in when it is announced
        _timer.Start();
        StartWatching();
    }

    /// Keeps the regular scan at <see cref="Timing.MixerRefresh"/> and announces every scan while the handle is held (a view that shows the
    /// mixer is on screen: the Volume page, an FX window, the tray panel); otherwise the mixer scans every <see cref="Timing.MixerRefreshIdle"/>. UI thread.
    public IDisposable WatchClosely() => _closeWatch.Take();

    private void OnCloseWatchChanged()
    {
        _timer.Interval = _closeWatch.Held ? Timing.MixerRefresh : Timing.MixerRefreshIdle;
        if (_closeWatch.Held) Scan(); // a view just came on screen: fresh values at once, not after the long interval
    }

    public AppAudioEntry? Find(string key) => Settings.Apps.FirstOrDefault(a => a.Key == key);

    /// The program paths the mixer remembers (the rule and automation pickers offer them as programs to choose).
    public IEnumerable<string> RememberedPaths() => Settings.Apps.Select(a => a.ExePath);

    /// True when the user moved this program's controls a moment ago, so a scan must not overwrite them.
    public bool RecentlyChanged(string key) =>
        _userChange.TryGetValue(key, out var t) && Grace.Within(t, Timing.MixerUserChangeGrace);

    private string? PathOf(int pid)
    {
        if (!_paths.TryGetValue(pid, out var p)) _paths[pid] = p = CoreAudio.ProcessPath(pid);
        return p;
    }

    /// Live peak levels (0 to 1) of the master (key null) or of a program that is open; false when there is nothing to read.
    public bool Peaks(string? key, out float left, out float right)
    {
        left = right = 0;
        if (key is null) return _meters.Peaks(MasterDeviceId, false, out left, out right);
        if (!_sessions.TryGetValue(key, out var list)) return false;
        foreach (var s in list)
            if (s.Peaks(out var l, out var r)) { left = Math.Max(left, l); right = Math.Max(right, r); }
        return true;
    }

    /// UI thread: takes what a scan read (<see cref="ScanRead"/>, made on a worker) into the mixer: the device lists (when read), the programs'
    /// sessions (the session objects themselves are made again here only when a program opened, closed, started or stopped playing,
    /// since Windows' session objects can't be used from another thread), saved settings put back on programs that just opened, and
    /// volumes changed elsewhere taken in. Raises <see cref="Updated"/> when any of that differs, or when asked (see the Scan part).
    private void Apply(ScanRead read)
    {
        var (devicesBefore, recordingBefore, runningBefore) = (Devices, RecordingDevices, Running);
        if (read.Devices is not null && read.RecordingDevices is not null && read.Ticket > _devicesTicket)
        {
            _devicesTicket = read.Ticket;
            (Devices, RecordingDevices) = (read.Devices, read.RecordingDevices);
            _devicesKnown = true;
            RememberNames();
        }
        var devicesChanged = !Devices.SequenceEqual(devicesBefore) || !RecordingDevices.SequenceEqual(recordingBefore);
        if (read.Ticket < _appliedTicket) // an older read that finished after a newer one: only its devices can be newer
        {
            if (devicesChanged) Updated?.Invoke();
            return;
        }
        _appliedTicket = read.Ticket;
        var announce = read.Announce || _closeWatch.Held || devicesChanged;
        try
        {
            foreach (var (pid, path) in read.Paths) _paths.TryAdd(pid, path);
            if (!SameSessions(read.Sessions))
            {
                Regroup();
                announce = true;
            }

            var changed = false;
            foreach (var (key, list) in _sessions)
            {
                var entry = Find(key);
                if (entry is null)
                {
                    // First time this program is seen: remember it as it is now.
                    var path = PathOf(list[0].ProcessId)!;
                    entry = new AppAudioEntry
                    {
                        Key = key, ExePath = path, Name = ProgramFile.NameOf(path),
                        Volume = list[0].Volume, Pan = list[0].Pan, Muted = list[0].Muted,
                    };
                    Settings.Apps.Add(entry);
                    foreach (var s in list) _applied.Add(s.ProcessId);
                    changed = true;
                    continue;
                }

                if (!string.Equals(entry.ExePath, PathOf(list[0].ProcessId), StringComparison.OrdinalIgnoreCase))
                {
                    // The program was updated and now lives in a new folder: follow it so its icon and name stay right.
                    entry.ExePath = PathOf(list[0].ProcessId)!;
                    changed = true;
                }

                foreach (var s in list.Where(s => _applied.Add(s.ProcessId)))
                {
                    // A program just opened (or OmniControl just started): put its saved settings back.
                    if (!Holds.IsSilenced(key))
                    {
                        s.SetVolume(entry.Volume);
                        s.SetMuted(entry.Muted);
                        if (entry.Pan != 0) s.SetPan(entry.Pan);
                    }
                    if (Holds.IsRoutingHeld(key)) Holds.KeepRouted(key, s.ProcessId);
                    else ApplyDirect(key, s.ProcessId);
                }
                Holds.KeepSilenced(key, list);

                if (!RecentlyChanged(key) && !Holds.IsSilenced(key))
                {
                    var v = list[0].Volume;
                    var m = list[0].Muted;
                    var p = list[0].CanPan ? list[0].Pan : entry.Pan;
                    if (entry.Volume != v || entry.Muted != m || entry.Pan != p) { entry.Volume = v; entry.Muted = m; entry.Pan = p; changed = true; }
                }
            }
            if (changed) Settings.Save();

            Running = Settings.Apps
                .Select(a => _sessions.TryGetValue(a.Key, out var l)
                    ? Holds.IsSilenced(a.Key)
                        ? new RunningApp(a.Key, true, l.Any(s => s.IsPlaying), a.Volume, a.Pan, a.Muted) // the session is held silent: the user's values live in the entry
                        : new RunningApp(a.Key, true, l.Any(s => s.IsPlaying), l[0].Volume, a.Pan, l[0].Muted)
                    : new RunningApp(a.Key, false, false, a.Volume, a.Pan, a.Muted))
                .ToList();
            announce |= changed || !Running.SequenceEqual(runningBefore);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            Guard.Log("Scanning programs and devices", ex); // keep the last known state
            announce = true;
        }
        SyncSoundPoll();
        if (announce) Updated?.Invoke();
    }

    /// Makes the programs' session objects again on this (UI) thread and groups them by program; forgets what belonged to processes that are gone.
    private void Regroup()
    {
        var live = CoreAudio.Sessions().Where(s => s.ProcessId != Environment.ProcessId).ToList(); // OmniControl's own effects playback is not a program row
        _heldSessions = SessionKey.Sorted(live.Select(SessionKey.Of));
        _sessions.Clear();
        var livePids = new HashSet<int>();
        foreach (var s in live)
        {
            livePids.Add(s.ProcessId);
            if (PathOf(s.ProcessId) is not { } path) continue;
            var key = AppAudioEntry.KeyFor(path);
            if (IgnoredPrograms.Contains(key)) continue;
            if (!_sessions.TryGetValue(key, out var list)) _sessions[key] = list = new();
            list.Add(s);
        }
        _applied.IntersectWith(livePids);
        foreach (var pid in _paths.Keys.Where(p => !livePids.Contains(p)).ToList()) _paths.Remove(pid);
    }

    // ---- changes made by the user ----

    public void SetVolume(string key, int percent)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Volume = Percent.Clamp(percent);
        if (!Holds.IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetVolume(e.Volume);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    public void SetMuted(string key, bool muted)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Muted = muted;
        if (!Holds.IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetMuted(muted);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    public void SetPan(string key, int pan)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Pan = PanMath.Clamp(pan);
        if (!Holds.IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetPan(e.Pan);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    // ---- FX chains (the effects engine runs them; edits apply live through FxChanged) ----

    /// The chain of the master (key null) or a program. Edit the list in place, then call <see cref="SaveFx"/>.
    public List<FxSlot> FxChain(string? key) =>
        key is null ? Settings.MasterFx : IsInput(key) ? InputEntry(key).Fx : CombinedOf(key) is { } combo ? combo.Fx : Find(key)?.Fx ?? new List<FxSlot>();

    public bool IsFxOn(string? key) =>
        key is null ? Settings.MasterFxOn : IsInput(key) ? InputEntry(key).FxOn : CombinedOf(key) is { } combo ? combo.FxOn : Find(key)?.FxOn ?? false;

    // ---- recording devices: their FX chains use the key "input:" + endpoint id ----

    /// The prefix of a recording device's key (its FX chain and engine row).
    public const string InputPrefix = "input:";

    public static bool IsInput(string? key) => key is not null && key.StartsWith(InputPrefix, StringComparison.Ordinal);

    /// The recording device's endpoint id in an input key.
    public static string InputIdOf(string key) => key[InputPrefix.Length..];

    /// The FX key of a recording device; remembers its name so the FX window can title itself.
    public string InputKey(string deviceId, string name)
    {
        var key = InputPrefix + deviceId;
        var entry = InputEntry(key);
        if (entry.Name != name) { entry.Name = name; Settings.Save(); }
        return key;
    }

    public string InputName(string key) => InputEntry(key).Name;

    internal InputFxEntry InputEntry(string key)
    {
        var id = InputIdOf(key);
        var entry = Settings.Inputs.FirstOrDefault(i => i.Id == id);
        if (entry is null) Settings.Inputs.Add(entry = new InputFxEntry { Id = id, Name = "Input" });
        return entry;
    }

    /// The user switched this row's effects off themselves (adding an effect then leaves them off).
    public bool IsFxUserOff(string? key) =>
        key is null ? Settings.MasterFxUserOff : IsInput(key) ? InputEntry(key).FxUserOff : CombinedOf(key) is { } combo ? combo.FxUserOff : Find(key)?.FxUserOff ?? false;

    /// The effects switch as the user flips it: remembers an "off" as the user's own choice. Everything automatic uses <see cref="SetFxOn"/>.
    public void SetFxByUser(string? key, bool on)
    {
        SetFxUserOff(key, !on);
        SetFxOn(key, on);
    }

    /// An effect was added to the chain: switches the effects on unless the user switched them off themselves.
    public void EffectsAdded(string? key)
    {
        if (SwitchesOnWhenAdded(IsFxOn(key), IsFxUserOff(key))) SetFxOn(key, true);
    }

    /// The rule: an added effect switches the effects on when they are off and the user did not switch them off.
    public static bool SwitchesOnWhenAdded(bool on, bool userOff) => !on && !userOff;

    private void SetFxUserOff(string? key, bool off)
    {
        if (key is null) Settings.MasterFxUserOff = off;
        else if (IsInput(key)) InputEntry(key).FxUserOff = off;
        else if (CombinedOf(key) is { } combo) combo.FxUserOff = off;
        else if (Find(key) is { } e) e.FxUserOff = off;
    }

    /// Switches a row's effects (the engine and Volume row react). Turning them on, by whoever, also clears "the user switched them off".
    public void SetFxOn(string? key, bool on)
    {
        if (on) SetFxUserOff(key, false);
        if (key is null) Settings.MasterFxOn = on;
        else if (IsInput(key)) InputEntry(key).FxOn = on;
        else if (CombinedOf(key) is { } combo) combo.FxOn = on;
        else if (Find(key) is { } e) e.FxOn = on;
        Settings.Save();
        FxChanged?.Invoke(key);
        SyncSoundPoll();
        Updated?.Invoke();
    }

    public void Save() => Settings.Save();

    /// The real speakers the master bus plays on now (kept so a crash or an exit can put them back); saved only when it changed.
    internal void KeepRealMasterDevice(string id)
    {
        if (Settings.RealMasterDevice == id) return;
        Settings.RealMasterDevice = id;
        Save();
    }

    /// The cable the master bus owns now (empty: none); saved only when it changed.
    internal void KeepBusCable(string cable)
    {
        if (Settings.BusCable == cable) return;
        Settings.BusCable = cable;
        Save();
    }

    /// What the last capture test found (saved).
    internal void KeepCaptureSilence(string result)
    {
        Settings.CaptureSilence = result;
        Save();
    }

    /// "Keep OmniControl's effects in place" (<see cref="MixerSettings.KeepFxInPlace"/>).
    public bool KeepFxInPlace => Settings.KeepFxInPlace;

    /// The setting changed (here, from the FX window, App settings or a rule). UI thread.
    public event Action? KeepFxInPlaceChanged;

    public void SetKeepFxInPlace(bool on)
    {
        if (Settings.KeepFxInPlace == on) return;
        Settings.KeepFxInPlace = on;
        Settings.Save();
        KeepFxInPlaceChanged?.Invoke();
    }

    /// Saves after an edit of a chain (or of one of its effects) and tells the running engine at once.
    public void SaveFx(string? key)
    {
        Settings.Save();
        FxChanged?.Invoke(key);
        SyncSoundPoll();
    }

    /// A chain or its on/off changed (key null = the master). Raised on the UI thread.
    public event Action<string?>? FxChanged;

    /// The user changed a program's volume, pan, mute or output device (the engine applies them while it holds the program).
    public event Action<string>? ProgramChanged;

    /// Adds a program the user picked (it needn't be running); its row stays until the user removes it. Returns its key.
    public string AddProgram(string path)
    {
        var key = AppAudioEntry.KeyFor(path);
        if (Find(key) is { } known) known.Added = true;
        else Settings.Apps.Add(new AppAudioEntry { Key = key, ExePath = path, Name = ProgramFile.NameOf(path), Added = true });
        Settings.Save();
        ScanAll();
        return key;
    }

    /// Whether the user's own "Add program" keeps this program's row (the restore of audio defaults and its undo); the row then follows <see cref="AppAudioEntry.KeepsRow"/>.
    public void SetAdded(string key, bool added)
    {
        if (Find(key) is not { } e || e.Added == added) return;
        e.Added = added;
        Settings.Save();
        Updated?.Invoke();
    }

    /// Forgets a remembered program that is not open (the row's remove button, the FX window's Forget program).
    public void Forget(string key)
    {
        if (_sessions.ContainsKey(key)) return; // still open
        Remove(key);
        ScanAll();
    }

    /// The one way a program's entry leaves the settings.
    private void Remove(string key)
    {
        Settings.Apps.RemoveAll(a => a.Key == key);
        _direct.Remove(key);
        Settings.Save();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _closeWatch.Changed -= OnCloseWatchChanged;
        StopWatching();
        _meters.Dispose();
    }

    /// Live peak levels (0 to 1) of a recording device; false when there is nothing to read.
    public bool InputPeaks(string deviceId, out float left, out float right) => _meters.Peaks(deviceId, true, out left, out right);
}
