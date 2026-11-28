using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays;

/// Night light as its cards, actions and states see it: a live on/off switch that warms immediately, the temperature it warms to,
/// and an optional schedule (its automation rule in <see cref="WarmLightScheduler"/>) that flicks the switch on and off at the chosen times.
/// Layers over each display's own colour temperature: it warms towards the target and never cools. Used on the UI thread.
internal sealed class WarmLightModel
{
    public const string Label = "Night light", ScheduleLabel = "Schedule night light", StartLabel = "Turn on at", EndLabel = "Turn off at", TargetLabel = "Temperature";

    private readonly WarmLightScheduler _scheduler;
    private readonly DisplaySettingsStore _store;
    private readonly FeatureRulesSlot _rules;

    public WarmLightModel(WarmLightScheduler scheduler, DisplaySettingsStore store, FeatureRulesSlot rules) => (_scheduler, _store, _rules) = (scheduler, store, rules);

    /// Night light or its schedule settings changed (the scheduler's own notice).
    public event Action? Changed
    {
        add => _scheduler.Changed += value;
        remove => _scheduler.Changed -= value;
    }

    /// The times offered, by name (half hours).
    public static IReadOnlyList<string> TimeNames { get; } = WarmSchedule.Times().Select(WarmSchedule.Clock).ToList();

    public bool IsOn => _scheduler.IsOn;

    public bool IsScheduled => _scheduler.IsScheduled;

    public string NightNote => _scheduler.NightNote;

    public string ScheduleNote => _scheduler.ScheduleNote;

    public string StartName => Nearest(_scheduler.Window.Start);
    public string EndName => Nearest(_scheduler.Window.End);

    /// The Night light schedule rule as it is now, or null.
    public FeatureRuleView? Rule => _scheduler.Rule;

    public void EditRule() => _scheduler.EditRule();
    public int Kelvin => Math.Clamp(_store.WarmLight.Kelvin, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral);

    public void Toggle() => SetOn(!IsOn);

    /// Turns Night light on or off immediately (the switch, tray tile, action, state and "Restore defaults").
    public void SetOn(bool on) => _scheduler.SetOn(on);

    public void ToggleScheduled() => SetScheduled(!IsScheduled);

    /// Turns the automatic schedule rule on or off without directly toggling Night light; a deleted rule is made again when turned on.
    public void SetScheduled(bool on)
    {
        if (_rules.Current is not { } rules) return;
        if (_scheduler.Rule is null) rules.Ensure(WarmLightScheduler.FadeId, WarmLightScheduler.RuleName, on, Window());
        else rules.SetOn(WarmLightScheduler.FadeId, on);
    }

    public void SetStart(string name) => SetTime(name, t => _store.WarmLight.Start = t);

    public void SetEnd(string name) => SetTime(name, t => _store.WarmLight.End = t);

    private FeatureTrigger.Between Window() => new(_store.WarmLight.Start, _store.WarmLight.End);

    public OperationResult SetKelvin(int kelvin)
    {
        _store.WarmLight.Kelvin = kelvin;
        _store.SaveWarmLight();
        _scheduler.Update();
        return OperationResult.Ok();
    }

    /// Night light and schedule off, with the default times and temperature.
    public void Reset()
    {
        var defaults = new WarmLightSettings();
        var settings = _store.WarmLight;
        (settings.Start, settings.End, settings.Kelvin) = (defaults.Start, defaults.End, defaults.Kelvin);
        _store.SaveWarmLight();
        SetScheduled(false);
        _rules.Current?.SetTrigger(WarmLightScheduler.FadeId, Window());
        _scheduler.Stop();
    }

    /// The page's times: kept as the times last chosen and put in the rule's window.
    private void SetTime(string name, Action<int> set)
    {
        var (start, end) = _scheduler.Window;
        (_store.WarmLight.Start, _store.WarmLight.End) = (start, end);
        set(WarmSchedule.Times().First(t => WarmSchedule.Clock(t) == name));
        _store.SaveWarmLight();
        _rules.Current?.SetTrigger(WarmLightScheduler.FadeId, Window());
    }

    /// The offered time closest to a saved one (the lists offer half hours; a hand-edited file may hold other minutes).
    private static string Nearest(int minute) => WarmSchedule.Clock(WarmSchedule.Times().MinBy(t => Math.Abs(t - minute)));
}
