namespace OmniControl.App.Features.Displays;

/// The warm-light schedule as its cards, action and state see it: on or off, the two times, the temperature it warms to, and what it is
/// doing now. The schedule layers over each display's own colour temperature (<see cref="WarmLightScheduler"/>): it warms towards the
/// target and never cools. Used on the UI thread.
internal sealed class WarmLightModel
{
    public const string Label = "Warm light at night", StartLabel = "Warm from", EndLabel = "Warm until", TargetLabel = "Warm light temperature";

    private readonly WarmLightScheduler _scheduler;
    private readonly DisplaySettingsStore _store;

    public WarmLightModel(WarmLightScheduler scheduler, DisplaySettingsStore store) => (_scheduler, _store) = (scheduler, store);

    /// The schedule or its settings changed (the scheduler's own notice).
    public event Action? Changed
    {
        add => _scheduler.Changed += value;
        remove => _scheduler.Changed -= value;
    }

    /// The times offered, by name (half hours).
    public static IReadOnlyList<string> TimeNames { get; } = WarmSchedule.Times().Select(WarmSchedule.Clock).ToList();

    public bool IsOn => _store.WarmLight.Enabled;

    /// "Off", "Active until 07:00" or "Starts at 21:00".
    public string StateText => _scheduler.StateText;

    public string StartName => Nearest(_store.WarmLight.Start);
    public string EndName => Nearest(_store.WarmLight.End);
    public int Kelvin => Math.Clamp(_store.WarmLight.Kelvin, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral);

    public void Toggle() => SetEnabled(!IsOn);

    /// Turns the schedule on or off (the button, its action, its state and "Restore defaults").
    public void SetEnabled(bool on)
    {
        if (_store.WarmLight.Enabled == on) return;
        _store.WarmLight.Enabled = on;
        Save();
    }

    public void SetStart(string name) => SetTime(name, t => _store.WarmLight.Start = t);

    public void SetEnd(string name) => SetTime(name, t => _store.WarmLight.End = t);

    public OperationResult SetKelvin(int kelvin)
    {
        _store.WarmLight.Kelvin = kelvin;
        Save();
        return OperationResult.Ok();
    }

    /// Off, with the default times and temperature.
    public void Reset()
    {
        var defaults = new WarmLightSettings();
        var settings = _store.WarmLight;
        (settings.Enabled, settings.Start, settings.End, settings.Kelvin) = (defaults.Enabled, defaults.Start, defaults.End, defaults.Kelvin);
        Save();
    }

    private void SetTime(string name, Action<int> set)
    {
        set(WarmSchedule.Times().First(t => WarmSchedule.Clock(t) == name));
        Save();
    }

    private void Save()
    {
        _store.SaveWarmLight();
        _scheduler.Update();
    }

    /// The offered time closest to a saved one (the lists offer half hours; a hand-edited file may hold other minutes).
    private static string Nearest(int minute) => WarmSchedule.Clock(WarmSchedule.Times().MinBy(t => Math.Abs(t - minute)));
}
