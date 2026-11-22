using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays;

/// Runs warm light. It is one automation rule ("Warm light": between the two times, do <see cref="FadeId"/>), read through the feature rules: the
/// rule's action starts the fade through the one run path, and the rule's end (its window passed, it was switched off, deleted, or rules were
/// paused) stops it. While fading, every <see cref="Timing.WarmLightTick"/> it works out how warm the picture should be (<see cref="WarmSchedule"/>:
/// eased in and out over the window, or eased in from the start when another trigger started it) and tells the <see cref="SoftwareDisplayService"/>.
/// The clock ticks only while fading. Used on the UI thread.
public sealed class WarmLightScheduler : IDisposable
{
    /// The warm-light rule's own action ("Warm light fade to 3400 K").
    public const string FadeId = "displays.sw.warmlight.fade";

    /// The rule's name when warm light makes it.
    public const string RuleName = "Warm light";

    private readonly SoftwareDisplayService _service;
    private readonly DisplaySettingsStore _store;
    private readonly FeatureRulesSlot _rules;
    private readonly Func<DateTime> _now;
    private DispatcherTimer? _timer;
    private bool _fading;
    private DateTime _since;

    /// <param name="rules">The automation rules warm light runs through (empty while automation is off).</param>
    /// <param name="now">The clock; a test gives its own.</param>
    public WarmLightScheduler(SoftwareDisplayService service, DisplaySettingsStore store, FeatureRulesSlot rules, Func<DateTime>? now = null)
    {
        (_service, _store, _rules) = (service, store, rules);
        _now = now ?? (() => DateTime.Now);
        rules.Changed += Update;
        rules.Ended += OnEnded;
    }

    /// The state changed (a tick moved the warming, the rule or the settings did); raised on the UI thread.
    public event Action? Changed;

    /// The rule as it is now, or null (deleted, or automation off).
    public FeatureRuleView? Rule => _rules.Current?.Find(FadeId);

    public bool Enabled => Rule?.On == true;

    /// The rule's window; while it has none, the times last chosen on the page.
    public (int Start, int End) Window => Rule?.Window is { } w ? (w.Start, w.End) : (Settings.Start, Settings.End);

    /// "Off", "Active until 07:00" or "Starts at 21:00".
    public string StateText => WarmSchedule.StateText(Enabled, Minute(), Window.Start, Window.End);

    private WarmLightSettings Settings => _store.WarmLight;

    private double Minute() => _now().TimeOfDay.TotalMinutes;

    /// Makes the clock, makes the rule from the saved schedule the first time (once: a deleted rule stays deleted) and applies.
    public void Start()
    {
        _timer ??= new DispatcherTimer(Timing.WarmLightTick, DispatcherPriority.Background, (_, _) => Update());
        if (!Settings.InRule && _rules.Current is { } rules)
        {
            rules.Ensure(FadeId, RuleName, Settings.Enabled, new FeatureTrigger.Between(Settings.Start, Settings.End));
            Settings.InRule = true;
            _store.SaveWarmLight();
        }
        Update();
    }

    /// The rule's action ran: warming starts now.
    public void Fade()
    {
        (_fading, _since) = (true, _now());
        Update();
    }

    /// Ends warming at once (the switch turned off, Restore defaults).
    public void Stop()
    {
        _fading = false;
        Update();
    }

    private void OnEnded(string actionId)
    {
        if (actionId == FadeId) Stop();
    }

    /// Applies the warming for the time now (on every tick and change); not fading, the clock stops.
    public void Update()
    {
        var factor = !_fading ? 0
            : Rule?.Window is { } w ? WarmSchedule.Factor(Minute(), w.Start, w.End)
            : Math.Clamp((_now() - _since).TotalMinutes / WarmSchedule.RampMinutes, 0, 1);
        _service.SetWarmth(factor, Settings.Kelvin);
        if (_fading) _timer?.Start();
        else _timer?.Stop();
        Changed?.Invoke();
    }

    /// Opens Automation where the rule is.
    public void EditRule() => _rules.Current?.Edit(FadeId);

    public void Dispose()
    {
        _timer?.Stop();
        _rules.Changed -= Update;
        _rules.Ended -= OnEnded;
    }
}
