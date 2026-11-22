using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays;

/// The warm-light schedule as its cards, action and state see it: on or off, the two times, the temperature it warms to, and what it is
/// doing now. On or off and the times are its automation rule's (<see cref="WarmLightScheduler"/>), the temperature its own; it layers over each
/// display's own colour temperature: it warms towards the target and never cools. Used on the UI thread.
internal sealed class WarmLightModel
{
    public const string TurnOn = "Turn on", TurnOff = "Turn off", Label = "Warm light at night", StartLabel = "Warm from", EndLabel = "Warm until", TargetLabel = "Warm light temperature";

    /// The words of the on/off button of the first card.
    public static readonly IReadOnlyList<string> StateButtons = new[] { TurnOn, TurnOff };

    private readonly WarmLightScheduler _scheduler;
    private readonly DisplaySettingsStore _store;
    private readonly FeatureRulesSlot _rules;

    public WarmLightModel(WarmLightScheduler scheduler, DisplaySettingsStore store, FeatureRulesSlot rules) => (_scheduler, _store, _rules) = (scheduler, store, rules);

    /// The schedule or its settings changed (the scheduler's own notice).
    public event Action? Changed
    {
        add => _scheduler.Changed += value;
        remove => _scheduler.Changed -= value;
    }

    /// The times offered, by name (half hours).
    public static IReadOnlyList<string> TimeNames { get; } = WarmSchedule.Times().Select(WarmSchedule.Clock).ToList();

    public bool IsOn => _scheduler.Enabled;

    /// "Off", "Active until 07:00" or "Starts at 21:00".
    public string StateText => _scheduler.StateText;

    public string StartName => Nearest(_scheduler.Window.Start);
    public string EndName => Nearest(_scheduler.Window.End);

    /// What was added to the rule in Automation ("1 extra condition"; empty: nothing).
    public string Extras => _scheduler.Rule?.Extras ?? string.Empty;

    public void EditRule() => _scheduler.EditRule();
    public int Kelvin => Math.Clamp(_store.WarmLight.Kelvin, SoftwareColor.TemperatureMin, SoftwareColor.TemperatureNeutral);

    public void Toggle() => SetEnabled(!IsOn);

    /// Switches the rule on or off (the button, its action, its state and "Restore defaults"); a deleted rule is made again when turned on.
    public void SetEnabled(bool on)
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

    /// Off, with the default times and temperature.
    public void Reset()
    {
        var defaults = new WarmLightSettings();
        var settings = _store.WarmLight;
        (settings.Start, settings.End, settings.Kelvin) = (defaults.Start, defaults.End, defaults.Kelvin);
        _store.SaveWarmLight();
        SetEnabled(false);
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
