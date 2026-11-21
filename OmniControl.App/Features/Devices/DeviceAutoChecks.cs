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
    private bool _running, _started;

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

    /// Starts the start-up check (a few seconds after the window is up) and, while a device has a periodic auto-check, the timer that keeps them; once.
    public void Start()
    {
        if (_started) return;
        _started = true;
        FollowNeed();
        DispatcherTimer.RunOnce(() => _ = RunAsync(atStart: true), Timing.DeviceStartCheckDelay);
    }

    /// The devices or their choices changed (UI thread): the periodic timer runs only while a device has an auto-check that repeats.
    public void FollowNeed()
    {
        var periodic = _started && _settings.Devices.Any(d => Of(d) is { } choice && AutoChecks.Of(choice).Every is not null);
        if (periodic == _timer is not null) return;
        if (periodic)
        {
            _timer = new DispatcherTimer { Interval = Timing.DeviceAutoCheckTick };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        else
        {
            _timer!.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private void OnTick(object? sender, EventArgs e) => _ = RunAsync(atStart: false);

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
        FollowNeed();
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
            // Python and python-kasa are looked for at start only when a device uses Scripts (else when the Devices page first opens).
            if (atStart && _settings.Devices.Any(d => d.Methods.Any(m => !m.IsSimulation))) await _setup.RefreshAsync();
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
