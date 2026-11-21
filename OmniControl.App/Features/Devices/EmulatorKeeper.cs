namespace OmniControl.App.Features.Devices;

/// Keeps the simulation method's emulator up for as long as the user chose (<see cref="EmulatorKeep"/>): an action wakes it when it is not
/// running (its window hidden when it can be, quick boot after one good cold boot, one cold boot as the fallback), waits until Android has started, and
/// afterwards the idle timer or "close after each action" shuts it down cleanly so its snapshot is saved. Starting, adb and memory
/// are Core's (<see cref="Emulators"/>, <see cref="AdbClient"/>); this decides when.
public sealed class EmulatorKeeper : IDisposable
{
    private readonly Func<string?> _adb;
    private readonly Action _save;
    private readonly Action<string> _log;
    private readonly Action _changed;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Timer _idle;
    private EmulatorInfo? _lastEmulator;
    private string _lastAddress = string.Empty;

    public EmulatorKeeper(EmulatorKeepSettings settings, Func<string?> adbPath, Action save, Action<string> log, Action changed)
    {
        Settings = settings;
        _adb = adbPath;
        _save = save;
        _log = log;
        _changed = changed;
        _idle = new Timer(_ => _ = CloseIdleAsync());
    }

    public EmulatorKeepSettings Settings { get; }

    /// How long the emulator may sit idle before it is closed ("until idle"); saved and followed at once.
    public void SetIdleMinutes(int minutes)
    {
        Settings.IdleMinutes = minutes;
        Edited();
    }

    /// True when OmniControl hid the running emulator's window, false when it is shown; null when it is not running or its window is not OmniControl's to hide.
    public bool? Hidden { get; private set; }

    public void Edited()
    {
        _save();
        _changed();
    }

    /// Turns on waking the emulator at start-up; that needs OmniControl itself in Windows' start-up list (the app's one start-up entry). Null when it worked, otherwise one plain sentence.
    public string? SetStartWithWindows(bool on)
    {
        Settings.StartWithWindows = on;
        Edited();
        return on && StartupRegistration.State != StartupState.On && !StartupRegistration.Set(true) ? "Windows refused to add OmniControl to its start-up list." : null;
    }

    /// At start-up: wakes the emulator when the user asked for that.
    public async Task WakeAtStartAsync(EmulatorInfo emulator, string address)
    {
        if (!Settings.StartWithWindows) return;
        if (await EnsureRunningAsync(emulator, address) is { } problem) _log($"Waking the emulator at start-up: {problem}");
        else ActionDone(emulator, address);
    }

    /// Makes sure the emulator is up and ready before an action; with <paramref name="show"/> (set-up) it must have its window, so a stopped one starts with it
    /// and a hidden one restarts with it; otherwise a stopped one starts hidden where it can be. <paramref name="progress"/> hears what is happening. Null when it is up, otherwise one plain sentence.
    public async Task<string?> EnsureRunningAsync(EmulatorInfo emulator, string address, Action<string>? progress = null, bool show = false)
    {
        _idle.Change(Timeout.Infinite, Timeout.Infinite);
        return await Exclusively(() => EnsureAsync(emulator, address, show, progress));
    }

    /// Starts the emulator hidden where it can be (a click on the Simulation row's chip) and, with "until idle", lets the idle timer close it again; "close after each action" is about actions, so it stays up. Null when it is up, otherwise one plain sentence.
    public async Task<string?> StartAsync(EmulatorInfo emulator, string address, Action<string>? progress = null)
    {
        var problem = await EnsureRunningAsync(emulator, address, progress);
        if (problem is null)
        {
            (_lastEmulator, _lastAddress) = (emulator, address);
            if (Settings.Keep == EmulatorKeep.UntilIdle) ArmIdle();
        }
        return problem;
    }

    /// True while a start, show or close is being done (the state's "acting" mark).
    public bool Busy => _one.CurrentCount == 0;

    private async Task<T> Exclusively<T>(Func<Task<T>> job)
    {
        await _one.WaitAsync();
        _changed();
        try
        {
            return await job();
        }
        finally
        {
            _one.Release();
            _changed();
        }
    }

    /// An action ended: closes the emulator, or starts the idle timer, as the user chose.
    public void ActionDone(EmulatorInfo emulator, string address)
    {
        (_lastEmulator, _lastAddress) = (emulator, address);
        switch (Settings.Keep)
        {
            case EmulatorKeep.AfterEachAction: _ = CloseNowAsync(emulator, address); break;
            case EmulatorKeep.UntilIdle: ArmIdle(); break;
        }
    }

    private void ArmIdle() => _idle.Change(TimeSpan.FromMinutes(Math.Max(1, Settings.IdleMinutes)), Timeout.InfiniteTimeSpan);

    /// Shuts the emulator down cleanly; null when it is closed, otherwise one plain sentence.
    public async Task<string?> CloseNowAsync(EmulatorInfo emulator, string address)
    {
        _idle.Change(Timeout.Infinite, Timeout.Infinite);
        return await Exclusively(() => ShutDownAsync(emulator, address));
    }

    /// Shows the emulator's window: starts the emulator (with it) when it is not running, otherwise un-hides the window and brings it to the front. Null when done, otherwise one plain sentence.
    public async Task<string?> ShowAsync(EmulatorInfo emulator, string address, Action<string> progress)
    {
        if (_adb() is not { } adb) return Emulators.Open(emulator); // nothing to wait for or close without adb
        _idle.Change(Timeout.Infinite, Timeout.Infinite);
        return await Exclusively(async () =>
        {
            if (!await IsUpAsync(adb, address)) return await EnsureAsync(emulator, address, show: true, progress);
            return Reveal(emulator) ? null : $"{emulator.Name}'s window wasn't found. Open it from the Start menu.";
        });
    }

    /// Hides the running emulator's window again (set-up closed); it keeps running.
    public async Task HideWindowAsync(EmulatorInfo emulator, string address)
    {
        if (!emulator.CanHide || _adb() is not { } adb) return;
        await _one.WaitAsync();
        try
        {
            if (await IsUpAsync(adb, address) && Emulators.SetWindowVisible(emulator, false))
            {
                Hidden = true;
                _changed();
            }
        }
        finally { _one.Release(); }
    }

    private bool Reveal(EmulatorInfo emulator)
    {
        if (emulator.CanHide) Emulators.SetWindowVisible(emulator, true);
        if (emulator.CanHide) Hidden = false;
        _changed();
        return Emulators.BringToFront(emulator);
    }

    /// Hides the window of an emulator that was just started, as soon as it has one (it opens a moment after the program starts).
    private async Task HideWhenOpenAsync(EmulatorInfo emulator)
    {
        var until = DateTime.UtcNow + Timing.EmulatorWindowWait;
        while (DateTime.UtcNow < until && !Emulators.SetWindowVisible(emulator, false)) await Task.Delay(Timing.EmulatorWindowPoll);
    }

    private async Task CloseIdleAsync()
    {
        if (_lastEmulator is { } emulator && await CloseNowAsync(emulator, _lastAddress) is { } problem) _log($"Closing the idle emulator: {problem}");
    }

    private async Task<bool> IsUpAsync(string adb, string address)
    {
        if (address.Contains(':') && (await AdbClient.ConnectAsync(adb, address)).Ok) return true;
        return (await AdbClient.OnlineSerialsAsync(adb)).Contains(address);
    }

    private async Task<string?> EnsureAsync(EmulatorInfo emulator, string address, bool show, Action<string>? progress)
    {
        if (_adb() is not { } adb) return "adb isn't installed yet.";
        if (await IsUpAsync(adb, address))
        {
            if (show) Reveal(emulator); // set-up needs the window: un-hide it, no restart
            return null;
        }
        var quick = Settings.ColdBootWorked;
        progress?.Invoke("Starting the emulator…");
        if (Emulators.Open(emulator, quick) is { } problem) return problem;
        Hidden = emulator.CanHide ? false : null;
        _log($"Waking {emulator.Name} ({(quick ? "quick boot" : "cold boot")}{(show || !emulator.CanHide ? string.Empty : ", window hidden")})");
        if (!show && emulator.CanHide)
        {
            await HideWhenOpenAsync(emulator);
            Hidden = true;
        }
        _changed();
        progress?.Invoke("Waiting for Android to start…");
        var ready = await WaitReadyAsync(adb, address, quick ? Timing.EmulatorQuickBootTimeout : Timing.EmulatorBootTimeout);
        if (!ready && quick)
        {
            _log($"{emulator.Name} did not finish starting from its snapshot; trying one cold boot.");
            Settings.ColdBootWorked = false;
            _save();
            progress?.Invoke("Starting again from scratch… (this takes longer)");
            Emulators.Kill(emulator);
            await Task.Delay(Timing.EmulatorBootPoll);
            if (Emulators.Open(emulator, quickBoot: false) is { } again) return again;
            if (!show && emulator.CanHide) await HideWhenOpenAsync(emulator);
            ready = await WaitReadyAsync(adb, address, Timing.EmulatorBootTimeout);
            quick = false;
        }
        if (!ready)
        {
            Hidden = null;
            _changed();
            return $"{emulator.Name} didn't finish starting in {Timing.EmulatorBootTimeout.TotalMinutes:0} minutes.";
        }
        if (!quick && !Settings.ColdBootWorked)
        {
            Settings.ColdBootWorked = true;
            _save();
        }
        _changed();
        progress?.Invoke("Connected");
        return null;
    }

    private async Task<bool> WaitReadyAsync(string adb, string address, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await IsUpAsync(adb, address)) break;
            await Task.Delay(Timing.EmulatorBootPoll);
        }
        return DateTime.UtcNow < until && await AdbClient.WaitForBootAsync(adb, address, until - DateTime.UtcNow, Timing.EmulatorBootPoll, CancellationToken.None);
    }

    private async Task<string?> ShutDownAsync(EmulatorInfo emulator, string address)
    {
        if (_adb() is not { } adb) return "adb isn't installed yet.";
        if (!(await AdbClient.OnlineSerialsAsync(adb)).Contains(address))
        {
            Hidden = null;
            _changed();
            return null;
        }
        var result = await AdbClient.ShutDownAsync(adb, address);
        _log($"Closing {emulator.Name}: {(result.Ok ? "done" : result.Text.Trim().ReplaceLineEndings(" | "))}");
        if (!result.Ok) return $"{emulator.Name} didn't close. Close it from its own window.";
        for (var i = 0; i < 15 && (await AdbClient.OnlineSerialsAsync(adb)).Contains(address); i++) await Task.Delay(Timing.EmulatorBootPoll);
        Hidden = null;
        _changed();
        return null;
    }

    public void Dispose()
    {
        _idle.Dispose();
        _one.Dispose();
    }
}
