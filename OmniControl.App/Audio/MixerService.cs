using OmniControl.Core.Models;
using OmniControl.App.Services;
using System.Diagnostics;
using Avalonia.Threading;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>A program as the mixer sees it right now.</summary>
public sealed record RunningApp(string Key, bool IsOpen, bool IsPlaying, int Volume, int Pan, bool Muted);

/// <summary>
/// Tracks the programs playing sound, remembers each one's volume, output device and EQ in <see cref="MixerSettings"/>,
/// and re-applies them when a program opens again.
/// </summary>
public sealed partial class MixerService : IDisposable
{
    /// <summary>The key of the system master in the Volume page and the FX windows (programs use their own key; the master chain itself is addressed by null).</summary>
    public const string MasterKey = "master";

    private readonly DispatcherTimer _timer = new() { Interval = Timing.MixerRefresh };
    private readonly Dictionary<string, List<AudioSession>> _sessions = new();
    private readonly Dictionary<int, string?> _paths = new();
    private readonly HashSet<int> _applied = new();
    private readonly Dictionary<string, DateTime> _userChange = new();

    public MixerSettings Settings { get; } = MixerSettings.Load();
    /// <summary>Playback devices plugged in and enabled now (virtual cables included).</summary>
    public IReadOnlyList<AudioDevice> Devices { get; private set; } = Array.Empty<AudioDevice>();

    /// <summary>Recording devices plugged in and enabled now (virtual cables' recording sides included).</summary>
    public IReadOnlyList<AudioDevice> RecordingDevices { get; private set; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<RunningApp> Running { get; private set; } = Array.Empty<RunningApp>();

    /// <summary>Raised after every scan (programs opened or closed, devices changed, volumes moved elsewhere).</summary>
    public event Action? Updated;

    /// <summary>Reports problems the user should know about (a device change Windows refused).</summary>
    public Action<string>? Report { get; set; }

    public MixerService()
    {
        _timer.Tick += (_, _) => Scan();
        foreach (var key in Settings.Apps.Select(a => a.Key).Where(IgnoredPrograms.Contains).ToList()) Remove(key); // remembered by an older version
        Scan();
        _timer.Start();
        StartWatching();
    }

    public AppAudioEntry? Find(string key) => Settings.Apps.FirstOrDefault(a => a.Key == key);

    /// <summary>True when the user moved this program's controls a moment ago, so a scan must not overwrite them.</summary>
    public bool RecentlyChanged(string key) =>
        _userChange.TryGetValue(key, out var t) && Grace.Within(t, Timing.MixerUserChangeGrace);

    private string? PathOf(int pid)
    {
        if (!_paths.TryGetValue(pid, out var p)) _paths[pid] = p = CoreAudio.ProcessPath(pid);
        return p;
    }

    /// <summary>Live peak levels (0 to 1) of the master (key null) or of a program that is open; false when there is nothing to read.</summary>
    public bool Peaks(string? key, out float left, out float right)
    {
        left = right = 0;
        if (key is null) return CoreAudio.DevicePeaks(MasterDeviceId, false, out left, out right);
        if (!_sessions.TryGetValue(key, out var list)) return false;
        foreach (var s in list)
            if (s.Peaks(out var l, out var r)) { left = Math.Max(left, l); right = Math.Max(right, r); }
        return true;
    }

    public void Scan()
    {
        try
        {
            Devices = CoreAudio.Devices();
            RecordingDevices = CoreAudio.InputDevices();
            RememberNames();
            var live = CoreAudio.Sessions();

            _sessions.Clear();
            var livePids = new HashSet<int>();
            foreach (var s in live)
            {
                if (s.ProcessId == Environment.ProcessId) continue; // OmniControl's own effects playback is not a program row
                livePids.Add(s.ProcessId);
                if (PathOf(s.ProcessId) is not { } path) continue;
                var key = AppAudioEntry.KeyFor(path);
                if (IgnoredPrograms.Contains(key)) continue;
                if (!_sessions.TryGetValue(key, out var list)) _sessions[key] = list = new();
                list.Add(s);
            }
            _applied.IntersectWith(livePids);
            foreach (var pid in _paths.Keys.Where(p => !livePids.Contains(p)).ToList()) _paths.Remove(pid);

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
                    if (!IsSilenced(key))
                    {
                        s.SetVolume(entry.Volume);
                        s.SetMuted(entry.Muted);
                        if (entry.Pan != 0) s.SetPan(entry.Pan);
                    }
                    if (IsRoutingHeld(key)) KeepRouted(key, s.ProcessId);
                    else ApplyDirect(key, s.ProcessId);
                }
                KeepSilenced(key, list);

                if (!RecentlyChanged(key) && !IsSilenced(key))
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
                    ? IsSilenced(a.Key)
                        ? new RunningApp(a.Key, true, l.Any(s => s.IsPlaying), a.Volume, a.Pan, a.Muted) // the session is held silent: the user's values live in the entry
                        : new RunningApp(a.Key, true, l.Any(s => s.IsPlaying), l[0].Volume, a.Pan, l[0].Muted)
                    : new RunningApp(a.Key, false, false, a.Volume, a.Pan, a.Muted))
                .ToList();
        }
        catch { /* keep the last known state */ }
        Updated?.Invoke();
    }

    // ---- changes made by the user ----

    public void SetVolume(string key, int percent)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Volume = Percent.Clamp(percent);
        if (!IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetVolume(e.Volume);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    public void SetMuted(string key, bool muted)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Muted = muted;
        if (!IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetMuted(muted);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    public void SetPan(string key, int pan)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Pan = PanMath.Clamp(pan);
        if (!IsSilenced(key) && _sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetPan(e.Pan);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    // ---- FX chains (the effects engine runs them; edits apply live through FxChanged) ----

    /// <summary>The chain of the master (key null) or a program. Edit the list in place, then call <see cref="SaveFx"/>.</summary>
    public List<FxSlot> FxChain(string? key) =>
        key is null ? Settings.MasterFx : IsInput(key) ? InputEntry(key).Fx : CombinedOf(key) is { } combo ? combo.Fx : Find(key)?.Fx ?? new List<FxSlot>();

    public bool IsFxOn(string? key) =>
        key is null ? Settings.MasterFxOn : IsInput(key) ? InputEntry(key).FxOn : CombinedOf(key) is { } combo ? combo.FxOn : Find(key)?.FxOn ?? false;

    // ---- recording devices: their FX chains use the key "input:" + endpoint id ----

    /// <summary>The prefix of a recording device's key (its FX chain and engine row).</summary>
    public const string InputPrefix = "input:";

    public static bool IsInput(string? key) => key is not null && key.StartsWith(InputPrefix, StringComparison.Ordinal);

    /// <summary>The recording device's endpoint id in an input key.</summary>
    public static string InputIdOf(string key) => key[InputPrefix.Length..];

    /// <summary>The FX key of a recording device; remembers its name so the FX window can title itself.</summary>
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

    /// <summary>The user switched this row's effects off themselves (adding an effect then leaves them off).</summary>
    public bool IsFxUserOff(string? key) =>
        key is null ? Settings.MasterFxUserOff : IsInput(key) ? InputEntry(key).FxUserOff : CombinedOf(key) is { } combo ? combo.FxUserOff : Find(key)?.FxUserOff ?? false;

    /// <summary>The effects switch as the user flips it: remembers an "off" as the user's own choice. Everything automatic uses <see cref="SetFxOn"/>.</summary>
    public void SetFxByUser(string? key, bool on)
    {
        SetFxUserOff(key, !on);
        SetFxOn(key, on);
    }

    /// <summary>An effect was added to the chain: switches the effects on unless the user switched them off themselves.</summary>
    public void EffectsAdded(string? key)
    {
        if (SwitchesOnWhenAdded(IsFxOn(key), IsFxUserOff(key))) SetFxOn(key, true);
    }

    /// <summary>The rule: an added effect switches the effects on when they are off and the user did not switch them off.</summary>
    public static bool SwitchesOnWhenAdded(bool on, bool userOff) => !on && !userOff;

    private void SetFxUserOff(string? key, bool off)
    {
        if (key is null) Settings.MasterFxUserOff = off;
        else if (IsInput(key)) InputEntry(key).FxUserOff = off;
        else if (CombinedOf(key) is { } combo) combo.FxUserOff = off;
        else if (Find(key) is { } e) e.FxUserOff = off;
    }

    /// <summary>Switches a row's effects (the engine and Volume row react). Turning them on, by whoever, also clears "the user switched them off".</summary>
    public void SetFxOn(string? key, bool on)
    {
        if (on) SetFxUserOff(key, false);
        if (key is null) Settings.MasterFxOn = on;
        else if (IsInput(key)) InputEntry(key).FxOn = on;
        else if (CombinedOf(key) is { } combo) combo.FxOn = on;
        else if (Find(key) is { } e) e.FxOn = on;
        Settings.Save();
        FxChanged?.Invoke(key);
        Updated?.Invoke();
    }

    public void Save() => Settings.Save();

    /// <summary>Saves after an edit of a chain (or of one of its effects) and tells the running engine at once.</summary>
    public void SaveFx(string? key)
    {
        Settings.Save();
        FxChanged?.Invoke(key);
    }

    /// <summary>A chain or its on/off changed (key null = the master). Raised on the UI thread.</summary>
    public event Action<string?>? FxChanged;

    /// <summary>The user changed a program's volume, pan, mute or output device (the engine applies them while it holds the program).</summary>
    public event Action<string>? ProgramChanged;

    /// <summary>Adds a program the user picked (it needn't be running); its row stays until the user removes it. Returns its key.</summary>
    public string AddProgram(string path)
    {
        var key = AppAudioEntry.KeyFor(path);
        if (Find(key) is { } known) known.Added = true;
        else Settings.Apps.Add(new AppAudioEntry { Key = key, ExePath = path, Name = ProgramFile.NameOf(path), Added = true });
        Settings.Save();
        Scan();
        return key;
    }

    /// <summary>Whether the user's own "Add program" keeps this program's row (the restore of audio defaults and its undo); the row then follows <see cref="AppAudioEntry.KeepsRow"/>.</summary>
    public void SetAdded(string key, bool added)
    {
        if (Find(key) is not { } e || e.Added == added) return;
        e.Added = added;
        Settings.Save();
        Updated?.Invoke();
    }

    /// <summary>Forgets a remembered program that is not open (the row's remove button, the FX window's Forget program).</summary>
    public void Forget(string key)
    {
        if (_sessions.ContainsKey(key)) return; // still open
        Remove(key);
        Scan();
    }

    /// <summary>The one way a program's entry leaves the settings.</summary>
    private void Remove(string key)
    {
        Settings.Apps.RemoveAll(a => a.Key == key);
        _direct.Remove(key);
        Settings.Save();
    }

    public void Dispose()
    {
        _timer.Stop();
        StopWatching();
    }
}
