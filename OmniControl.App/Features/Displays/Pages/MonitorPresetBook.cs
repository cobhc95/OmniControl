using OmniControl.App.Features.Displays;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Displays.Pages;

/// The Hardware group's presets: for each monitor connected when saved, the DDC/CI values it reports (<see cref="MonitorPresetRules"/>),
/// kept in <see cref="DisplaySettingsStore"/>. Applying writes each connected monitor that has an entry (monitors without one are left
/// alone, entries of monitors that are away stay saved) and remembers, per monitor, the preset it got last: when several presets
/// match what the monitors show, the one remembered for a connected monitor is the one named. The displays are read through
/// <see cref="IMonitorPresetTarget"/>, so nothing here touches a monitor.
internal sealed class MonitorPresetBook : IPresetBook, IDisposable
{
    public const string NoControls = "No monitor controls to save";

    private readonly DisplaySettingsStore _store;
    private readonly Func<IReadOnlyList<IMonitorPresetTarget>> _targets;
    private int _last = -1;

    /// <param name="targets">The displays now (read each time: they come and go).</param>
    public MonitorPresetBook(DisplaySettingsStore store, Func<IReadOnlyList<IMonitorPresetTarget>> targets)
    {
        (_store, _targets) = (store, targets);
        store.Monitors.Changed += Refresh;
    }

    public string Label => "Monitor presets";

    public PresetBookText Text { get; } = new("+ Save the monitors' current settings as a preset",
        "Keeps the monitor's own settings (brightness, contrast, colour, input...) of every connected monitor as a new preset", "Monitors");

    public IReadOnlyList<INamedPreset> Presets => _store.Monitors.Items;

    public IReadOnlyList<string> Names => _store.Monitors.Items.Select(p => p.Name).ToList();

    public string? UnavailableReason => MonitorPresetRules.HasControls(Live()) ? null : NoControls;

    public string? Shown
    {
        get
        {
            var live = Live();
            var matching = _store.Monitors.Items.Where(p => MonitorPresetRules.Matches(p, live)).ToList();
            var remembered = matching.FirstOrDefault(p => live.Any(m => _store.LastMonitorPreset(m.Name) is { } last && PresetRules.Same(last, p.Name)));
            return (remembered ?? matching.FirstOrDefault())?.Name;
        }
    }

    /// What the monitors show now.
    private List<LiveMonitor> Live() => _targets().Select(t => t.Snapshot()).OfType<LiveMonitor>().ToList();

    public void Apply(string name)
    {
        if (_store.Monitors.Find(name) is not { } preset) return;
        var targets = _targets();
        var live = targets.Select(t => t.Snapshot()).OfType<LiveMonitor>().ToList();
        var pairs = MonitorPresetRules.Pair(preset.Monitors, live);
        foreach (var (monitor, entry) in pairs)
        {
            var writes = MonitorPresetRules.Plan(entry, monitor);
            if (writes.Count > 0) targets.First(t => t.Id == monitor.Id).Write(writes);
        }
        if (pairs.Count > 0) _store.RememberMonitorPreset(preset.Name, pairs.Select(p => p.Monitor.Name));
        Refresh();
    }

    public void SaveCurrent(string name)
    {
        var live = Live();
        var preset = new MonitorPreset { Name = name.Trim(), Monitors = MonitorPresetRules.Capture(live) };
        // Replacing a preset keeps what it held for monitors that are away now.
        if (_store.Monitors.Find(name) is { } old)
        {
            var present = MonitorPresetRules.Pair(old.Monitors, live).Select(p => p.Entry).ToList();
            preset.Monitors.AddRange(old.Monitors.Where(e => !present.Contains(e)));
        }
        _store.Monitors.Save(preset);
        _store.RememberMonitorPreset(preset.Name, live.Where(m => preset.Monitors.Any(e => e.Name == m.Name)).Select(m => m.Name));
        Refresh(); // the remembered preset may change which one is named
    }

    public string? Rename(string from, string to) => _store.Monitors.Rename(from, to);

    public void Move(string name, int index) => _store.Monitors.Move(name, index);

    /// Deletes the preset; its undo puts the preset back (which monitor had it last is not brought back).
    public Action? Delete(string name) => _store.Monitors.Delete(name);

    public IReadOnlyList<PresetPart> PartsOf(string name)
    {
        if (_store.Monitors.Find(name) is not { } preset) return Array.Empty<PresetPart>();
        var present = MonitorPresetRules.Pair(preset.Monitors, Live()).Select(p => p.Entry).ToList();
        return preset.Monitors.Select(e => new PresetPart(e.Name, present.Contains(e))).ToList();
    }

    /// The "next monitor preset" action: the saved preset after the one applied last (the first one at the start).
    public void Next()
    {
        var names = Names;
        if (names.Count == 0) return;
        _last = (_last + 1) % names.Count;
        Apply(names[_last]);
    }

    public event Action? Changed;

    /// The monitors came, went or changed: the drop-down and the manager read them again.
    public void Refresh() => Changed?.Invoke();

    public void Dispose() => _store.Monitors.Changed -= Refresh;
}
