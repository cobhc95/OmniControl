using OmniControl.App.Audio;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>The Outputs list: the master, the programs (open ones first, then remembered ones), combined speakers, "Add program".</summary>
public sealed partial class VolumePageViewModel
{
    private readonly Dictionary<string, MixerItem> _items = new();
    private readonly AddProgramItem _add;
    private readonly MixerItem _master;
    private DateTime _masterChange;

    private MixerItem NewMaster() =>
        new(MixerService.MasterKey, true, Gate)
        {
            Name = "Master",
            VolumeChanged = v => { _masterChange = DateTime.UtcNow; _steps.Remember(MixerService.MasterKey, v); CoreAudio.SetMasterVolume(v); },
            PanChanged = p => { _masterChange = DateTime.UtcNow; CoreAudio.SetMasterPan(p); },
            MuteChanged = m => { _masterChange = DateTime.UtcNow; CoreAudio.SetMasterMute(m); },
            FxToggled = on => _mixer.SetFxOn(null, on),
            OpenWindowRequested = () => _openFx(null),
            WiringRequested = () => ShowWiring(MixerService.MasterKey),
        };

    private void SyncMaster()
    {
        CoreAudio.GetMaster(out var v, out var muted);
        _steps.Remember(MixerService.MasterKey, v);
        CoreAudio.GetMasterPan(out var pan);
        _master.Sync(true, v, pan, muted, _mixer.IsFxOn(null), _mixer.FxChain(null).Count);
        SyncRow(_master);
    }

    private MixerItem Create(AppAudioEntry entry)
    {
        var key = entry.Key;
        MixerItem? row = null;
        row = new MixerItem(key, false, Gate)
        {
            Name = entry.Name,
            Picture = ProcessIcons.Get(entry.ExePath),
            VolumeChanged = v => _mixer.SetVolume(key, v),
            PanChanged = p => _mixer.SetPan(key, p),
            MuteChanged = m => _mixer.SetMuted(key, m),
            FxToggled = on => _mixer.SetFxOn(key, on),
            OpenWindowRequested = () => _openFx(key),
            WiringRequested = () => ShowWiring(key),
            Forget = () => _mixer.Forget(key),
            Test = () => TestCaptureAsync(row!),
        };
        return row;
    }

    /// <summary>Updates a row from the latest scan. A row the user just moved keeps its own values for a few seconds.</summary>
    private void Fill(MixerItem item, RunningApp r, AppAudioEntry e)
    {
        var useSystem = r.IsOpen && !_mixer.RecentlyChanged(e.Key);
        var volume = useSystem ? r.Volume : r.IsOpen ? item.Volume : e.Volume;
        var muted = useSystem ? r.Muted : r.IsOpen ? item.Muted : e.Muted;
        var pan = useSystem ? r.Pan : item.Pan;
        item.Name = e.Name;
        item.Sync(r.IsOpen, volume, pan, muted, e.FxOn, e.Fx.Count);
        item.Lead = !r.IsOpen ? "Not open" : string.Empty;
        SyncRow(item);
    }

    /// <summary>
    /// Puts the Outputs list in order: master, programs that are open, remembered ones (each alphabetical), combined speakers, then
    /// "Add program". Rows that already exist are kept; rows of programs that are gone go.
    /// </summary>
    private void Rebuild()
    {
        var seen = new HashSet<string>();
        var ordered = new List<MixerItem>();
        foreach (var (r, e) in _mixer.Running
                     .Select(r => (r, e: _mixer.Find(r.Key)))
                     .Where(x => x.e is not null)
                     .OrderByDescending(x => x.r.IsOpen).ThenBy(x => x.e!.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            seen.Add(r.Key);
            if (!_items.TryGetValue(r.Key, out var item)) _items[r.Key] = item = Create(e!);
            Fill(item, r, e!);
            ordered.Add(item);
        }
        RemoveMissing(_items, seen);
        var combos = SyncCombos();

        var rows = new List<object> { _master };
        rows.AddRange(ordered);
        rows.AddRange(combos.Where(c => !c.IsMicrophone));
        rows.Add(_add);
        if (!rows.SequenceEqual(Outputs.Items))
        {
            Outputs.Items.Clear();
            foreach (var row in rows) Outputs.Items.Add(row);
        }
        SyncMaster();
        UpdateSummaries();
        SyncWiring();
    }

    /// <summary>After a scan: new or vanished programs put the list in order again; otherwise only the rows' values change.</summary>
    private void Merge()
    {
        var running = _mixer.Running.Where(r => _mixer.Find(r.Key) is not null).ToList();
        if (running.Count != _items.Count || running.Any(r => !_items.ContainsKey(r.Key)) || _mixer.Combined.Count != _combos.Count)
            Rebuild();
        else
        {
            foreach (var r in running) Fill(_items[r.Key], r, _mixer.Find(r.Key)!);
            SyncCombos();
        }

        if (!Grace.Within(_masterChange, Timing.SliderEchoGrace)) SyncMaster();
        else SyncRow(_master);
        RefreshInputs();
    }
}
