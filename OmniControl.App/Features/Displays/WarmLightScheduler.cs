using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays;

/// Runs Night light: a live master switch (<see cref="IsOn"/>) that applies or removes the warm tint immediately, plus an optional
/// automation schedule rule ("Night light": between the two times, do <see cref="FadeId"/>) that flicks the switch on when its window
/// begins and off when its window ends. Manual toggles take effect straight away regardless of the clock; turning the schedule off stops
/// future automatic switches without changing the current Night light state. Used on the UI thread.
public sealed class WarmLightScheduler : IDisposable
{
    /// The warm-light rule's own action ("Turn night light on (3400 K)").
    public const string FadeId = "displays.sw.warmlight.fade";

    /// The rule's name when Night light makes it.
    public const string RuleName = "Night light";

    private readonly SoftwareDisplayService _service;
    private readonly DisplaySettingsStore _store;
    private readonly FeatureRulesSlot _rules;

    /// <param name="rules">The automation rules Night light's schedule runs through.</param>
    public WarmLightScheduler(SoftwareDisplayService service, DisplaySettingsStore store, FeatureRulesSlot rules)
    {
        (_service, _store, _rules) = (service, store, rules);
        rules.Changed += Update;
        rules.Ended += OnEnded;
    }

    /// The state changed (Night light toggled, temperature changed, or the schedule rule changed); raised on the UI thread.
    public event Action? Changed;

    /// The rule as it is now, or null (deleted).
    public FeatureRuleView? Rule => _rules.Current?.Find(FadeId);

    /// True while Night light is currently warming the screen.
    public bool IsOn { get; private set; }

    /// True while the automatic schedule rule is enabled.
    public bool IsScheduled => Rule?.On == true;

    public bool IsWarming => IsOn;

    /// The rule's window; while it has none, the times last chosen on the page.
    public (int Start, int End) Window => Rule?.Window is { } w ? (w.Start, w.End) : (Settings.Start, Settings.End);

    /// Live status note for the Night light switch.
    public string NightNote => IsOn
        ? IsScheduled ? $"On · Turns off at {WarmSchedule.Clock(Window.End)}" : $"On · {Settings.Kelvin} K"
        : IsScheduled ? $"Off · Turns on at {WarmSchedule.Clock(Window.Start)}" : "Off";

    /// Live status note for the Schedule switch.
    public string ScheduleNote => IsScheduled
        ? $"Flicks Night light on at {WarmSchedule.Clock(Window.Start)} and off at {WarmSchedule.Clock(Window.End)}"
        : "Off · Night light only changes when you toggle it";

    private WarmLightSettings Settings => _store.WarmLight;

    /// Makes the schedule rule from the saved schedule the first time (once: a deleted rule stays deleted) and applies.
    public void Start()
    {
        if (!Settings.InRule && _rules.Current is { } rules)
        {
            rules.Ensure(FadeId, RuleName, Settings.Enabled, new FeatureTrigger.Between(Settings.Start, Settings.End));
            Settings.InRule = true;
            _store.SaveWarmLight();
        }
        Update();
    }

    /// Turns Night light on or off straight away (manual toggle, tray tile, hotkey, state, or schedule boundary).
    public void SetOn(bool on)
    {
        IsOn = on;
        Update();
    }

    /// The schedule rule's action ran: flick Night light on.
    public void Fade() => SetOn(true);

    /// Ends warming at once (Restore defaults).
    public void Stop() => SetOn(false);

    /// The schedule rule's window ended: flick Night light off only if the schedule itself is still on (turning the schedule toggle off leaves Night light as-is).
    private void OnEnded(string actionId)
    {
        if (actionId == FadeId && IsScheduled) SetOn(false);
    }

    /// Applies the current Night light state and temperature to every display and notifies listeners.
    public void Update()
    {
        _service.SetWarmth(IsOn ? 1 : 0, Settings.Kelvin);
        Changed?.Invoke();
    }

    /// Opens Automation where the rule is.
    public void EditRule() => _rules.Current?.Edit(FadeId);

    public void Dispose()
    {
        _rules.Changed -= Update;
        _rules.Ended -= OnEnded;
    }
}
