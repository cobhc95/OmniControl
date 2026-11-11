using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Volume page: a list with the master on top and one row per program (open or remembered), each with volume, pan, mute
/// and an FX power button. Sound state lives in <see cref="MixerService"/> and <see cref="CoreAudio"/>; the FX chain window
/// is opened through the <c>openFx</c> callback. This class only turns those into rows and keeps them up to date.
/// </summary>
public sealed class VolumePageViewModel : PageViewModel
{
    private readonly MixerService _mixer;
    private readonly OsdService _osd;
    private readonly Action<string?> _openFx;
    private readonly Dictionary<string, MixerItem> _items = new();
    private readonly AddProgramItem _add;
    private readonly MixerItem _master;
    private DateTime _masterChange;

    public override string Title => "Volume";
    public override string Icon => "M3,9 H7 L12,4 V20 L7,15 H3 Z M15,8.5 A5,5 0 0 1 15,15.5 L16.2,17 A7,7 0 0 0 16.2,7 Z";
    public override string Keywords => "volume sound audio mixer master apps programs pan balance mute fx effects chain equaliser eq compressor";

    /// <summary>Outputs: the master row, then every program, then the "Add program" row. Collapsed until the user opens it.</summary>
    public VolumeGroup Outputs { get; } = new("Volume/Outputs", "Outputs");

    /// <summary>Inputs: every recording device (microphones and so on). Collapsed until the user opens it.</summary>
    public VolumeGroup Inputs { get; } = new("Volume/Inputs", "Inputs");

    private ObservableCollection<object> Rows => Outputs.Items;
    private readonly Dictionary<string, InputItem> _inputs = new();
    private DateTime _inputChange;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _meterTimer = new() { Interval = Timing.MeterFrame };

    public override IEnumerable<SearchEntry> SearchEntries =>
        new[] { _master }.Concat(_items.Values)
            .Select(i => new SearchEntry { Title = "Volume", Category = "Volume", Editor = i, Keywords = "sound audio mixer app program pan fx" });

    /// <param name="openFx">Opens the FX chain window for a program key (null = master).</param>
    public VolumePageViewModel(MixerService mixer, HotkeyRegistry hotkeys, OsdService osd, Action<string> report, Action<string?> openFx)
    {
        _mixer = mixer;
        _osd = osd;
        _openFx = openFx;
        mixer.Report = report;

        _master = new MixerItem("master", true)
        {
            Name = "Master",
            VolumeChanged = v => { _masterChange = DateTime.UtcNow; CoreAudio.SetMasterVolume(v); },
            PanChanged = p => { _masterChange = DateTime.UtcNow; CoreAudio.SetMasterPan(p); },
            MuteChanged = m => { _masterChange = DateTime.UtcNow; CoreAudio.SetMasterMute(m); },
            FxOpenRequested = () => _openFx(null),
            FxToggled = on => _mixer.SetFxOn(null, on),
        };
        SyncMaster();

        _add = new AddProgramItem { Add = path => { var key = mixer.AddProgram(path); report($"Added {mixer.Find(key)?.Name}"); } };

        Rebuild();
        RefreshInputs();
        mixer.Updated += Merge;
        _meterTimer.Tick += (_, _) => FeedMeters();

        hotkeys.Register(new HotkeyAction("volume.mic.mute", "Toggle microphone mute", "Volume", "sound audio input microphone mic mute silence",
            _ => StepInput(0, toggleMute: true)));
        hotkeys.Register(new HotkeyAction("volume.mic.fx", "Toggle microphone FX", "Volume", "sound audio input microphone mic fx effects chain",
            _ => { if (CoreAudio.InputDevices().FirstOrDefault(d => d.IsDefault) is { } d && _inputs.TryGetValue(d.Id, out var i)) i.FxOn = !i.FxOn; }));
        hotkeys.Register(new HotkeyAction("volume.mic.up", "Microphone level up", "Volume", "sound audio input microphone mic louder", _ => StepInput(+5)));
        hotkeys.Register(new HotkeyAction("volume.mic.down", "Microphone level down", "Volume", "sound audio input microphone mic quieter", _ => StepInput(-5)));

        hotkeys.Register(new HotkeyAction("volume.up", "Volume up", "Volume", "sound audio master louder", _ => StepMaster(+5)));
        hotkeys.Register(new HotkeyAction("volume.down", "Volume down", "Volume", "sound audio master quieter", _ => StepMaster(-5)));
        hotkeys.Register(new HotkeyAction("volume.mute", "Toggle mute", "Volume", "sound audio master mute silence", _ => ToggleMasterMute()));
        hotkeys.Register(new HotkeyAction("volume.fx", "Toggle master FX", "Volume", "sound audio master fx effects chain equaliser",
            _ => _mixer.SetFxOn(null, !_mixer.IsFxOn(null))));
    }

    public override void OnShown()
    {
        _mixer.Scan();
        Rebuild();
        RefreshInputs();
    }

    // ---- inputs ----

    private void RefreshInputs()
    {
        var devices = CoreAudio.InputDevices();
        var seen = new HashSet<string>();
        foreach (var d in devices)
        {
            seen.Add(d.Id);
            if (!_inputs.TryGetValue(d.Id, out var item))
            {
                var id = d.Id;
                var fxKey = _mixer.InputKey(id, d.Name);
                item = new InputItem(id, fxKey)
                {
                    Name = d.Name,
                    FxOpenRequested = () => _openFx(fxKey),
                    FxToggled = on => _mixer.SetFxOn(fxKey, on),
                    VolumeChanged = v => { _inputChange = DateTime.UtcNow; CoreAudio.SetDeviceVolume(id, v); },
                    MuteChanged = m => { _inputChange = DateTime.UtcNow; CoreAudio.SetDeviceMute(id, m); },
                };
                _inputs[id] = item;
                Inputs.Items.Add(item);
            }
            item.Name = d.Name;
            item.SyncFx(_mixer.IsFxOn(item.FxKey), _mixer.FxChain(item.FxKey).Count);
            if (DateTime.UtcNow - _inputChange > Timing.SliderEchoGrace && CoreAudio.GetDeviceVolume(d.Id, out var v, out var muted))
                item.Sync(d.IsDefault ? "Default input" : string.Empty, v, muted);
        }
        foreach (var gone in _inputs.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Inputs.Items.Remove(_inputs[gone]);
            _inputs.Remove(gone);
        }
        UpdateSummaries();
    }

    private void UpdateSummaries()
    {
        var programs = Rows.OfType<MixerItem>().Count(i => !i.IsMaster);
        Outputs.Summary = programs == 1 ? "Master + 1 program" : $"Master + {programs} programs";
        Inputs.Summary = _inputs.Count == 1 ? "1 device" : $"{_inputs.Count} devices";
    }

    /// <summary>Hotkeys: change the level of, or mute, the default recording device.</summary>
    private void StepInput(int delta, bool toggleMute = false)
    {
        var def = CoreAudio.InputDevices().FirstOrDefault(d => d.IsDefault);
        if (def is null || !CoreAudio.GetDeviceVolume(def.Id, out var v, out var muted)) return;
        _inputChange = DateTime.UtcNow;
        if (toggleMute) muted = !muted;
        else
        {
            if (muted && delta > 0) muted = false;
            v = Math.Clamp(v + delta, 0, 100);
        }
        CoreAudio.SetDeviceVolume(def.Id, v);
        CoreAudio.SetDeviceMute(def.Id, muted);
        if (_inputs.TryGetValue(def.Id, out var item)) item.Sync(item.Detail, v, muted);
        _osd.Show(toggleMute ? (muted ? "Microphone muted" : "Microphone on") : "Microphone", muted ? 0 : v);
    }

    // ---- meters ----

    /// <summary>The page calls these while it is on screen; the meters only run then, and only for rows that are open.</summary>
    public void StartMeters() => _meterTimer.Start();

    public void StopMeters()
    {
        _meterTimer.Stop();
        foreach (var i in Rows.OfType<MixerItem>()) i.Meter.Clear();
        foreach (var i in _inputs.Values) i.Meter.Clear();
    }

    private void FeedMeters()
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (Outputs.IsExpanded)
        {
            foreach (var i in Rows.OfType<MixerItem>())
            {
                if (!i.IsMaster && !i.IsOpen) continue;
                _mixer.Peaks(i.IsMaster ? null : i.Key, out var l, out var r);
                i.Meter.Feed(l, r, now);
            }
        }
        if (Inputs.IsExpanded)
        {
            foreach (var i in _inputs.Values)
            {
                CoreAudio.DevicePeaks(i.Id, true, out var l, out var r);
                i.Meter.Feed(i.Muted ? 0 : l, i.Muted ? 0 : r, now);
            }
        }
    }

    // ---- master ----

    private void SyncMaster()
    {
        CoreAudio.GetMaster(out var v, out var muted);
        CoreAudio.GetMasterPan(out var pan);
        _master.Sync(CoreAudio.DefaultDeviceName(), true, v, pan, muted, _mixer.IsFxOn(null), _mixer.FxChain(null).Count);
    }

    private void StepMaster(int delta)
    {
        if (!CoreAudio.GetMaster(out var v, out var muted)) return;
        if (muted && delta > 0) CoreAudio.SetMasterMute(false);
        v = Math.Clamp(v + delta, 0, 100);
        CoreAudio.SetMasterVolume(v);
        _masterChange = DateTime.UtcNow;
        _master.Sync(_master.Detail, true, v, _master.Pan, false, _master.FxOn, _master.FxCount);
        _osd.Show("Volume", v);
    }

    private void ToggleMasterMute()
    {
        if (!CoreAudio.GetMaster(out var v, out var muted)) return;
        CoreAudio.SetMasterMute(!muted);
        _masterChange = DateTime.UtcNow;
        _master.Sync(_master.Detail, true, v, _master.Pan, !muted, _master.FxOn, _master.FxCount);
    }

    // ---- programs ----

    private static string StatusOf(RunningApp r, AppAudioEntry e)
    {
        var state = !r.IsOpen ? "Not open · settings saved" : r.IsPlaying ? "Playing" : "Open";
        return e.DeviceId.Length > 0 && e.DeviceName.Length > 0 ? $"{state} · {e.DeviceName}" : state;
    }

    private MixerItem Create(AppAudioEntry entry)
    {
        var key = entry.Key;
        return new MixerItem(key, false)
        {
            Name = entry.Name,
            Icon = ProcessIcons.Get(entry.ExePath),
            VolumeChanged = v => _mixer.SetVolume(key, v),
            PanChanged = p => _mixer.SetPan(key, p),
            MuteChanged = m => _mixer.SetMuted(key, m),
            FxOpenRequested = () => _openFx(key),
            FxToggled = on => _mixer.SetFxOn(key, on),
        };
    }

    /// <summary>Updates a row from the latest scan. A row the user just moved keeps its own values for a few seconds.</summary>
    private void Fill(MixerItem item, RunningApp r, AppAudioEntry e)
    {
        var useSystem = r.IsOpen && !_mixer.RecentlyChanged(e.Key);
        var volume = useSystem ? r.Volume : r.IsOpen ? item.Volume : e.Volume;
        var muted = useSystem ? r.Muted : r.IsOpen ? item.Muted : e.Muted;
        var pan = useSystem ? r.Pan : item.Pan;
        item.Name = e.Name;
        item.Sync(StatusOf(r, e), r.IsOpen, volume, pan, muted, e.FxOn, e.Fx.Count);
    }

    /// <summary>Rebuilds the list in order: master, programs that are open, remembered ones (each alphabetical), then "Add program".</summary>
    private void Rebuild()
    {
        _items.Clear();
        Rows.Clear();
        Rows.Add(_master);
        var rows = _mixer.Running
            .Select(r => (r, e: _mixer.Find(r.Key)))
            .Where(x => x.e is not null)
            .OrderByDescending(x => x.r.IsOpen).ThenBy(x => x.e!.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var (r, e) in rows)
        {
            var item = Create(e!);
            Fill(item, r, e!);
            _items[r.Key] = item;
            Rows.Add(item);
        }
        Rows.Add(_add);
        SyncMaster();
        UpdateSummaries();
    }

    /// <summary>Brings the rows up to date after a scan without disturbing the one being used.</summary>
    private void Merge()
    {
        var seen = new HashSet<string>();
        foreach (var r in _mixer.Running)
        {
            if (_mixer.Find(r.Key) is not { } e) continue;
            seen.Add(r.Key);
            if (!_items.TryGetValue(r.Key, out var item))
            {
                item = Create(e);
                _items[r.Key] = item;
                Rows.Insert(Rows.Count - 1, item); // before the "Add program" row
            }
            Fill(item, r, e);
        }
        foreach (var gone in _items.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Rows.Remove(_items[gone]);
            _items.Remove(gone);
        }

        if (DateTime.UtcNow - _masterChange > Timing.SliderEchoGrace) SyncMaster();
        RefreshInputs();
    }
}
