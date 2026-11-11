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
public sealed class MixerService : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = Timing.MixerRefresh };
    private readonly Dictionary<string, List<AudioSession>> _sessions = new();
    private readonly Dictionary<int, string?> _paths = new();
    private readonly HashSet<int> _applied = new();
    private readonly Dictionary<string, DateTime> _userChange = new();

    public MixerSettings Settings { get; } = MixerSettings.Load();
    public IReadOnlyList<AudioDevice> Devices { get; private set; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<RunningApp> Running { get; private set; } = Array.Empty<RunningApp>();

    /// <summary>Raised after every scan (programs opened or closed, devices changed, volumes moved elsewhere).</summary>
    public event Action? Updated;

    /// <summary>Reports problems the user should know about (a device change Windows refused).</summary>
    public Action<string>? Report { get; set; }

    public MixerService()
    {
        _timer.Tick += (_, _) => Scan();
        Scan();
        _timer.Start();
    }

    public static string KeyFor(string path) => path.ToLowerInvariant();

    public AppAudioEntry? Find(string key) => Settings.Apps.FirstOrDefault(a => a.Key == key);

    /// <summary>True when the user moved this program's controls a moment ago, so a scan must not overwrite them.</summary>
    public bool RecentlyChanged(string key) =>
        _userChange.TryGetValue(key, out var t) && DateTime.UtcNow - t < Timing.MixerUserChangeGrace;

    private static string NameOf(string path)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { /* fall through */ }
        var n = Path.GetFileNameWithoutExtension(path);
        return n.Length == 0 ? path : char.ToUpperInvariant(n[0]) + n[1..];
    }

    private string? PathOf(int pid)
    {
        if (!_paths.TryGetValue(pid, out var p)) _paths[pid] = p = CoreAudio.ProcessPath(pid);
        return p;
    }

    /// <summary>Live peak levels (0 to 1) of the master (key null) or of a program that is open; false when there is nothing to read.</summary>
    public bool Peaks(string? key, out float left, out float right)
    {
        left = right = 0;
        if (key is null) return CoreAudio.DevicePeaks(null, false, out left, out right);
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
            var live = CoreAudio.Sessions();

            _sessions.Clear();
            var livePids = new HashSet<int>();
            foreach (var s in live)
            {
                livePids.Add(s.ProcessId);
                if (PathOf(s.ProcessId) is not { } path) continue;
                var key = KeyFor(path);
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
                        Key = key, ExePath = path, Name = NameOf(path),
                        Volume = list[0].Volume, Pan = list[0].Pan, Muted = list[0].Muted,
                    };
                    Settings.Apps.Add(entry);
                    foreach (var s in list) _applied.Add(s.ProcessId);
                    changed = true;
                    continue;
                }

                foreach (var s in list.Where(s => _applied.Add(s.ProcessId)))
                {
                    // A program just opened (or OmniControl just started): put its saved settings back.
                    s.SetVolume(entry.Volume);
                    s.SetMuted(entry.Muted);
                    if (entry.Pan != 0) s.SetPan(entry.Pan);
                    ApplyDevice(entry, s.ProcessId);
                }

                if (!RecentlyChanged(key))
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
                    ? new RunningApp(a.Key, true, l.Any(s => s.IsPlaying), l[0].Volume, a.Pan, l[0].Muted)
                    : new RunningApp(a.Key, false, false, a.Volume, a.Pan, a.Muted))
                .ToList();
        }
        catch { /* keep the last known state */ }
        Updated?.Invoke();
    }

    private void ApplyDevice(AppAudioEntry entry, int pid)
    {
        if (string.IsNullOrEmpty(entry.DeviceId)) return; // never touch programs left on the default device
        var available = Devices.Any(d => d.Id == entry.DeviceId);
        if (!CoreAudio.TrySetAppDevice(pid, available ? entry.DeviceId : null, out var error))
            Report?.Invoke($"Couldn't move {entry.Name} to {entry.DeviceName}: {error}");
    }

    // ---- changes made by the user ----

    public void SetVolume(string key, int percent)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Volume = Math.Clamp(percent, 0, 100);
        if (_sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetVolume(e.Volume);
        Settings.Save();
    }

    public void SetMuted(string key, bool muted)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Muted = muted;
        if (_sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetMuted(muted);
        Settings.Save();
    }

    /// <summary>Chooses the playback device for a program; <paramref name="deviceId"/> empty = the default device.</summary>
    public void SetDevice(string key, string deviceId, string deviceName)
    {
        if (Find(key) is not { } e) return;
        e.DeviceId = deviceId;
        e.DeviceName = deviceName;
        Settings.Save();
        if (!_sessions.TryGetValue(key, out var list)) return;
        var available = deviceId.Length == 0 || Devices.Any(d => d.Id == deviceId);
        foreach (var pid in list.Select(s => s.ProcessId).Distinct())
            if (!CoreAudio.TrySetAppDevice(pid, available && deviceId.Length > 0 ? deviceId : null, out var error))
                Report?.Invoke($"Couldn't change the device for {e.Name}: {error}");
    }

    public void SetPan(string key, int pan)
    {
        if (Find(key) is not { } e) return;
        _userChange[key] = DateTime.UtcNow;
        e.Pan = Math.Clamp(pan, -100, 100);
        if (_sessions.TryGetValue(key, out var list)) foreach (var s in list) s.SetPan(e.Pan);
        Settings.Save();
    }

    // ---- FX chains (settings only: no sound engine applies them yet) ----

    /// <summary>The chain of the master (key null) or a program. Edit the list in place, then call <see cref="Save"/>.</summary>
    public List<FxSlot> FxChain(string? key) =>
        key is null ? Settings.MasterFx : IsInput(key) ? InputEntry(key).Fx : Find(key)?.Fx ?? new List<FxSlot>();

    public bool IsFxOn(string? key) => key is null ? Settings.MasterFxOn : IsInput(key) ? InputEntry(key).FxOn : Find(key)?.FxOn ?? false;

    // ---- recording devices: their FX chains use the key "input:" + endpoint id ----

    private const string InputPrefix = "input:";

    public static bool IsInput(string? key) => key is not null && key.StartsWith(InputPrefix, StringComparison.Ordinal);

    /// <summary>The FX key of a recording device; remembers its name so the FX window can title itself.</summary>
    public string InputKey(string deviceId, string name)
    {
        var key = InputPrefix + deviceId;
        var entry = InputEntry(key);
        if (entry.Name != name) { entry.Name = name; Settings.Save(); }
        return key;
    }

    public string InputName(string key) => InputEntry(key).Name;

    private InputFxEntry InputEntry(string key)
    {
        var id = key[InputPrefix.Length..];
        var entry = Settings.Inputs.FirstOrDefault(i => i.Id == id);
        if (entry is null) Settings.Inputs.Add(entry = new InputFxEntry { Id = id, Name = "Input" });
        return entry;
    }

    public void SetFxOn(string? key, bool on)
    {
        if (key is null) Settings.MasterFxOn = on;
        else if (IsInput(key)) InputEntry(key).FxOn = on;
        else if (Find(key) is { } e) e.FxOn = on;
        Settings.Save();
        Updated?.Invoke();
    }

    public void Save() => Settings.Save();

    /// <summary>Adds a program the user picked (it needn't be running). Returns its key.</summary>
    public string AddProgram(string path)
    {
        var key = KeyFor(path);
        if (Find(key) is null)
        {
            Settings.Apps.Add(new AppAudioEntry { Key = key, ExePath = path, Name = NameOf(path) });
            Settings.Save();
        }
        Scan();
        return key;
    }

    public void Forget(string key)
    {
        if (_sessions.ContainsKey(key)) return; // still open
        Settings.Apps.RemoveAll(a => a.Key == key);
        Settings.Save();
        Scan();
    }

    public void Dispose() => _timer.Stop();
}
