using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// The Outputs list: the master, the programs (open ones first, then remembered ones when "Show closed" is on) and combined speakers.
public sealed partial class VolumePageViewModel
{
    private readonly Dictionary<string, MixerItem> _items = new();
    /// The keys of the programs that are rows now (every program has an item; a closed one that is not listed keeps it, so its effects and status carry on).
    private HashSet<string> _listed = new();
    private readonly MixerItem _master;

    /// The words under a program's name: not open, or playing now (an open, silent program says nothing).
    private const string NotOpenWord = "not open", PlayingWord = "playing";

    /// The master's row. Its menus find the row when they open (it exists by then: it is made in the page's constructor).
    private MixerItem NewMaster() =>
        new(MixerService.MasterKey, true)
        {
            Name = "Master",
            VolumeChanged = v => { _actions.NoteMasterChange(v); CoreAudio.SetMasterVolume(v, _mixer.MasterDeviceId); },
            PanChanged = p => { _actions.NoteMasterChange(); CoreAudio.SetMasterPan(p, _mixer.MasterDeviceId); },
            MuteChanged = m => { _actions.NoteMasterChange(); CoreAudio.SetMasterMute(m, _mixer.MasterDeviceId); },
            FxToggled = on => _mixer.SetFxByUser(null, on),
            FxWindowToggled = () => _toggleFx(null),
            PlaysOn = new PlaysOnMenu(new ControlGate(), menu => OpenPlaysOn(menu, _master), marksCurrent: true),
            RouteChoice = RouteChoice.ForMaster(route => { _mixer.SetMasterMethod(route); SyncRow(_master); }, () => SyncRow(_master)),
        };

    private void SyncMaster()
    {
        CoreAudio.GetMaster(out var v, out var muted, _mixer.MasterDeviceId);
        _actions.RememberMaster(v);
        CoreAudio.GetMasterPan(out var pan, _mixer.MasterDeviceId);
        _master.Sync(true, v, pan, muted, _mixer.IsFxOn(null), FxSlot.EffectCount(_mixer.FxChain(null)));
        SyncRow(_master);
    }

    private bool IsListed(RunningApp r, AppAudioEntry e) =>
        ProgramListing.IsListed(r.IsOpen, e.KeepsRow, Outputs.ProgramTools?.ShowClosed ?? true);

    /// The "Add program" menu's choice: remembers the program as a row; a closed one needs "Show closed" to be seen, so it is switched on.
    private void AddProgram(string path)
    {
        var key = _mixer.AddProgram(path);
        if (Outputs.ProgramTools is { ShowClosed: false } tools && _mixer.Running.FirstOrDefault(r => r.Key == key) is { IsOpen: false }) tools.ShowClosed = true;
        _report($"Added {_mixer.Find(key)?.Name}");
    }

    private MixerItem Create(AppAudioEntry entry)
    {
        var key = entry.Key;
        MixerItem? row = null;
        row = new MixerItem(key, false)
        {
            Name = entry.Name,
            Picture = _icons.Get(entry.ExePath),
            VolumeChanged = v => _mixer.SetVolume(key, v),
            PanChanged = p => _mixer.SetPan(key, p),
            MuteChanged = m => _mixer.SetMuted(key, m),
            FxToggled = on => _mixer.SetFxByUser(key, on),
            FxWindowToggled = () => _toggleFx(key),
            PlaysOn = new PlaysOnMenu(new ControlGate(), menu => OpenPlaysOn(menu, row!), marksCurrent: true),
            Forget = () => _mixer.Forget(key),
            Test = () => TestCaptureAsync(row!),
            RouteChoice = RouteChoice.ForProgram(route => { _mixer.SetFxRoute(key, route); SyncRow(row!); }, () => SyncRow(row!)),
        };
        return row;
    }

    /// Updates a row from the latest scan. A row the user just moved keeps its own values for a few seconds.
    private void Fill(MixerItem item, RunningApp r, AppAudioEntry e)
    {
        var useSystem = r.IsOpen && !_mixer.RecentlyChanged(e.Key);
        var volume = useSystem ? r.Volume : r.IsOpen ? item.Volume : e.Volume;
        var muted = useSystem ? r.Muted : r.IsOpen ? item.Muted : e.Muted;
        var pan = useSystem ? r.Pan : item.Pan;
        item.Name = e.Name;
        item.Sync(r.IsOpen, volume, pan, muted, e.FxOn, FxSlot.EffectCount(e.Fx));
        item.TestWhy = EffectsRules.TestUnavailable(e.Name, r.IsPlaying);
        item.StateWord = !r.IsOpen ? NotOpenWord : r.IsPlaying ? PlayingWord : string.Empty;
        SyncRow(item);
    }

    /// Puts the Outputs list in order: master, programs that are open, remembered ones when "Show closed" is on (each alphabetical), combined speakers.
    /// Programs not listed (closed, and "Show closed" off or nothing worth remembering) keep their item but have no row. Rows that already exist are kept; rows of programs that are gone go.
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
            if (IsListed(r, e!)) ordered.Add(item);
        }
        _listed = ordered.Select(i => i.Key).ToHashSet();
        RemoveMissing(_items, seen);
        var combos = SyncCombos();

        var rows = new List<object> { _master };
        rows.AddRange(ordered);
        rows.AddRange(combos.Where(c => !c.IsMicrophone));
        if (!rows.SequenceEqual(Outputs.Items))
        {
            Outputs.Items.Clear();
            foreach (var row in rows) Outputs.Items.Add(row);
        }
        SyncMaster();
        UpdateSummaries();
        SyncWiring();
    }

    /// After a scan: new or vanished programs put the list in order again; otherwise only the rows' values change.
    private void Merge()
    {
        var running = _mixer.Running.Where(r => _mixer.Find(r.Key) is not null).ToList();
        if (running.Count != _items.Count || running.Any(r => !_items.ContainsKey(r.Key) || IsListed(r, _mixer.Find(r.Key)!) != _listed.Contains(r.Key))
            || _mixer.Combined.Count != _combos.Count)
            Rebuild();
        else
        {
            foreach (var r in running) Fill(_items[r.Key], r, _mixer.Find(r.Key)!);
            SyncCombos();
        }

        if (!_actions.MasterJustChanged) SyncMaster();
        else SyncRow(_master);
        RefreshInputs();
    }
}
