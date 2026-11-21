namespace OmniControl.App.Features.Displays;

/// Runs the warm-light schedule: every <see cref="Timing.WarmLightTick"/> while it is on (and at once when its settings change) it works out how far
/// into its window the clock is (<see cref="WarmSchedule"/>) and tells the <see cref="SoftwareDisplayService"/>, which warms every
/// display's own temperature towards the target and eases it back out. Used on the UI thread.
public sealed class WarmLightScheduler : IDisposable
{
    private readonly SoftwareDisplayService _service;
    private readonly DisplaySettingsStore _store;
    private readonly Func<DateTime> _now;
    private DispatcherTimer? _timer;

    /// <param name="now">The clock; a test gives its own.</param>
    public WarmLightScheduler(SoftwareDisplayService service, DisplaySettingsStore store, Func<DateTime>? now = null)
    {
        (_service, _store) = (service, store);
        _now = now ?? (() => DateTime.Now);
    }

    /// The state changed (a tick moved the warming, or the settings did); raised on the UI thread.
    public event Action? Changed;

    /// "Off", "Active until 07:00" or "Starts at 21:00".
    public string StateText => WarmSchedule.StateText(Settings.Enabled, Minute(), Settings.Start, Settings.End);

    private WarmLightSettings Settings => _store.WarmLight;

    private double Minute() => _now().TimeOfDay.TotalMinutes;

    /// Makes the clock and applies the schedule as it stands; the clock ticks only while the schedule is on.
    public void Start()
    {
        _timer ??= new DispatcherTimer(Timing.WarmLightTick, DispatcherPriority.Background, (_, _) => Update());
        Update();
    }

    /// Applies the schedule for the time now (after a setting changed, and on every tick); off, the clock stops.
    public void Update()
    {
        var factor = Settings.Enabled ? WarmSchedule.Factor(Minute(), Settings.Start, Settings.End) : 0;
        _service.SetWarmth(factor, Settings.Kelvin);
        if (Settings.Enabled) _timer?.Start();
        else _timer?.Stop();
        Changed?.Invoke();
    }

    public void Dispose() => _timer?.Stop();
}
