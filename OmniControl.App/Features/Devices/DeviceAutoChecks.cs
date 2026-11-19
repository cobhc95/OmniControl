namespace OmniControl.App.Features.Devices;

/// Reading devices' states on their own, from the service, so the tray tiles show real states without the page: at start-up, every so often,
/// when the page or tray panel opens and after a hardware change, as each device's auto-check choice says (<see cref="AutoChecks"/>). One
/// reading run at a time; the reading itself is the service's (Scripts only, never waking an emulator). UI thread.
public sealed class DeviceAutoChecks
{
    private readonly DeviceSettings _settings;
    private readonly DeviceSetup _setup;
    private readonly Action _edited;
    private readonly Func<DeviceRecord, Task> _check;
    private readonly Dictionary<string, DateTime> _lastAuto = new();
    private DispatcherTimer? _timer;
    private bool _running;

    /// <param name="edited">Saves the device list after a choice changed.</param>
    /// <param name="check">One automatic read of a device (it skips what is not ready or is in its set-up).</param>
    internal DeviceAutoChecks(DeviceSettings settings, DeviceSetup setup, Action edited, Func<DeviceRecord, Task> check)
    {
        _settings = settings;
        _setup = setup;
        _edited = edited;
        _check = check;
    }

    /// The device's auto-check changed (a pick or the switch): the page's chips read it again.
    public event Action? Changed;

    /// Starts the start-up check (a few seconds after the window is up) and the timer that keeps the periodic ones; once.
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = Timing.DeviceAutoCheckTick };
        _timer.Tick += (_, _) => _ = RunAsync(atStart: false);
        _timer.Start();
        DispatcherTimer.RunOnce(() => _ = RunAsync(atStart: true), Timing.DeviceStartCheckDelay);
    }

    /// The choice a device follows now (null = manual only).
    public AutoCheck? Of(DeviceRecord device) => AutoChecks.Effective(_settings, device);

    public void Pick(DeviceRecord device, AutoCheck value)
    {
        AutoChecks.Pick(_settings, device, value);
        Edited();
    }

    public bool OnTrayOf(DeviceRecord device) => AutoChecks.OnTray(_settings, device);

    public void PickOnTray(DeviceRecord device, bool value)
    {
        AutoChecks.PickOnTray(_settings, device, value);
        Edited();
    }

    public void SetSame(DeviceRecord device, bool same)
    {
        AutoChecks.SetSame(_settings, device, same);
        Edited();
    }

    private void Edited()
    {
        _edited();
        Changed?.Invoke();
    }

    /// The page opened: devices set to "When the page or tray panel opens" read their state (Scripts only).
    public async Task CheckWhenShownAsync()
    {
        foreach (var device in _settings.Devices.ToList())
            if (Of(device) is { } choice && AutoChecks.Of(choice).WhenShown) await _check(device);
    }

    /// Reads the state of every device that has an auto-check, due or not (the network may just have come back); one reading run at a time.
    public async Task CheckAllNowAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            foreach (var device in _settings.Devices.ToList())
            {
                if (Of(device) is null) continue;
                _lastAuto[device.Id] = DateTime.UtcNow;
                await _check(device);
            }
        }
        finally { _running = false; }
    }

    private async Task RunAsync(bool atStart)
    {
        if (_running) return;
        _running = true;
        try
        {
            if (atStart) await _setup.RefreshAsync(); // Python and python-kasa are looked for before the first read
            var now = DateTime.UtcNow;
            foreach (var device in _settings.Devices.ToList())
            {
                if (Of(device) is not { } value) continue;
                var choice = AutoChecks.Of(value);
                var due = atStart ? choice.AtStart : choice.Every is { } every && AutoChecks.Due(_lastAuto.TryGetValue(device.Id, out var last) ? last : null, now, every);
                if (!due) continue;
                _lastAuto[device.Id] = now;
                await _check(device);
            }
        }
        finally { _running = false; }
    }
}
