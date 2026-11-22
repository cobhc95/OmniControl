using OmniControl.Core.Engine;

namespace OmniControl.App.Features;

/// THE feature switch: which features of <see cref="FeatureList"/> run, kept as the ones switched off (so a feature added later starts on).
/// Setup's "Choose features" choice is taken once each time Setup writes a new one (<see cref="SetupFeatures"/>); App settings owns it otherwise.
/// The shell starts and stops the modules when it changes and says which features run now (<see cref="SetRunning"/>). UI thread.
public sealed class FeatureSwitches
{
    private readonly ISettingSlot<List<string>> _off;
    private readonly HashSet<string> _running = new();

    /// <param name="off">The features switched off (App settings' own copy).</param>
    /// <param name="fromSetup">Setup's choice as last taken, so the same choice is never taken twice.</param>
    public FeatureSwitches(ISettingSlot<List<string>> off, ISettingSlot<string?> fromSetup)
    {
        _off = off;
        if (SetupFeatures.Off() is not { } chosen || chosen == fromSetup.Value) return;
        fromSetup.Value = chosen;
        _off.Value = FeatureList.Parse(chosen);
    }

    /// The choice changed. The shell hears it first (it subscribes at start-up) and has started and stopped the modules before the others hear it.
    public event Action? Changed;

    public IReadOnlyCollection<string> Off => _off.Value;

    public bool IsOn(string id) => !_off.Value.Contains(id);

    /// The feature runs now (the shell's word).
    public bool Runs(string id) => _running.Contains(id);

    public void Set(string id, bool on)
    {
        if (on == IsOn(id)) return;
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
            if (runs) _running.Add(id);
            else _running.Remove(id);
    }
}
