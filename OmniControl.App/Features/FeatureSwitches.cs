using OmniControl.Core.Engine;

namespace OmniControl.App.Features;

/// THE feature switch: which features of <see cref="FeatureList"/> run, kept as the ones switched off (so a feature added later starts on).
/// Each Setup run that writes a choice (<see cref="SetupFeatures"/>) is taken once, over the switches; App settings owns it otherwise.
/// The shell starts and stops the modules when it changes and says what runs and what failed to start (<see cref="SetRunning"/>,
/// <see cref="SetFailed"/>), then <see cref="Settle"/>. UI thread.
public sealed class FeatureSwitches
{
    private readonly ISettingSlot<List<string>> _off;
    private readonly HashSet<string> _running = new(), _failed = new();
    private readonly HashSet<string> _missing = FeatureList.All.Where(f => f.File is not null && !OptionalFiles.Present(f.File)).Select(f => f.Id).ToHashSet();

    /// <param name="off">The features switched off (App settings' own copy).</param>
    /// <param name="fromSetup">The Setup run last taken (<see cref="SetupFeatures.Choice.Run"/>), so a run is never taken twice.</param>
    public FeatureSwitches(ISettingSlot<List<string>> off, ISettingSlot<string?> fromSetup)
    {
        _off = off;
        if (SetupFeatures.Read() is not { } chosen || chosen.Run == fromSetup.Value) return;
        fromSetup.Value = chosen.Run;
        _off.Value = FeatureList.Parse(chosen.Off);
    }

    /// The choice changed; only the shell follows it (it starts and stops the modules, then <see cref="Settle"/>).
    public event Action? Changed;

    /// The modules run as the choice says, as far as they could start: what shows the switches follows this.
    public event Action? Settled;

    public IReadOnlyCollection<string> Off => _off.Value;

    public bool IsOn(string id) => !_off.Value.Contains(id) && Installed(id);

    /// False when Setup left its file out ("Don't install them"): it never runs until Setup adds it.
    public bool Installed(string id) => !_missing.Contains(id);

    /// The feature runs now (the shell's word).
    public bool Runs(string id) => _running.Contains(id);

    /// Its last start failed (logged); it is tried again at the next change or start.
    public bool Failed(string id) => _failed.Contains(id);

    public void Set(string id, bool on)
    {
        if (on == IsOn(id) || !Installed(id)) return;
        SetOff(on ? _off.Value.Where(o => o != id) : _off.Value.Append(id));
    }

    /// Makes exactly these features off (a ready-made choice, Turn all on, Restore defaults).
    public void SetOff(IEnumerable<string> off)
    {
        var now = off.Where(id => FeatureList.Find(id) is not null).Distinct().ToList();
        if (now.Count == _off.Value.Count && now.All(_off.Value.Contains)) return;
        _off.Value = now;
        Changed?.Invoke();
    }

    /// The shell started or stopped these features.
    public void SetRunning(IEnumerable<string> ids, bool runs)
    {
        foreach (var id in ids)
        {
            if (runs) _running.Add(id);
            else _running.Remove(id);
            _failed.Remove(id);
        }
    }

    /// The shell could not start these features.
    public void SetFailed(IEnumerable<string> ids) => _failed.UnionWith(ids);

    /// The shell has done what the choice asks.
    public void Settle() => Settled?.Invoke();
}
