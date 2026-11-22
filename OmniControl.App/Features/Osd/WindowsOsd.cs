namespace OmniControl.App.Features.Osd;

/// Where Windows' pop-up was before OmniControl moved it off screen, so the next run can put it back if this one ended first.
public sealed class WindowsOsdRecord
{
    public long Window { get; set; }
    public int ProcessId { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Minimised { get; set; }
}

/// Best-effort suppression of Windows' own volume/brightness pop-up (Windows 11). Only ONE window is ever touched: the one learned as the
/// pop-up the first time it naturally opens after a real change. A candidate (<see cref="WindowsOsdWindows"/>: an Explorer XAML pop-up
/// window, checked by class, children, styles and owning program) is learned only when it (1) is seen opening while hiding is on (a window
/// already on screen when hiding starts never counts), (2) opens 0 ms to <see cref="Timing.WindowsOsdMatch"/> AFTER a volume, brightness,
/// microphone or media change made outside OmniControl (<see cref="Trigger"/>; never before it), (3) has the pop-up's size and place
/// (<see cref="WindowsOsdWindows.HasFlyoutShape"/>: a screen-sized host or a thumbnail preview is refused) and (4) opens while the pointer is
/// off the taskbar. Nothing is ever done to make the pop-up appear (no key presses, no change to the sound). It is hidden in layers: its
/// host is moved off screen and minimised (a minimised host never presents it, so nothing flashes; checked with one IsIconic per change);
/// if Windows un-minimises it, the event hook (<see cref="WindowsOsdGuard"/>) and the fast look catch it and it is minimised again. It is
/// never activated or re-ordered, and put back un-minimised at the exact place it had; where it was and that it was minimised is saved
/// (<see cref="DataStores.WindowsOsdHidden"/>) so a run that ended first is undone at the next start, only if that handle is still the same
/// pop-up. When more than <see cref="Timing.WindowsOsdMaxWindows"/> different windows have been learned in a run, hiding stops for the run.
/// Every decision is logged to %APPDATA%\OmniControl\osd-log.txt.
public sealed class WindowsOsd : IDisposable
{
    /// Lines written to osd-log.txt per run, so a stuck loop cannot grow the file.
    private const int MaxLogLines = 100;

    private readonly DispatcherTimer _timer = new() { Interval = Timing.WindowsOsdPoll };
    private readonly Dictionary<IntPtr, long> _openedAt = new(); // candidates seen opening while hiding is on, and when (tick count)
    private readonly HashSet<IntPtr> _taken = new();             // every window learned as the pop-up in this run (the kill switch counts them)
    private HashSet<IntPtr> _onScreen = new();                   // candidates on screen at the last look
    private long _lastChange = long.MinValue / 2;                // tick count of the last outside change Windows may show its pop-up for
    private IntPtr _flyout;                                      // the learned pop-up, or zero
    private int _flyoutProcess;
    private WindowPlace? _parkedFrom;                            // where the pop-up was when it was last moved off screen
    private bool _stopped;
    private bool _missLogged;                                    // the event hook missing a show is logged once per run
    private bool _unminimisedLogged;                             // Windows un-minimising the host is logged once per run
    private readonly WindowsOsdGuard _guard = new();             // parks the learned pop-up the instant Windows shows it
    private int _logged;

    public WindowsOsd() => _timer.Tick += (_, _) => Guard.Run("Hide Windows' OSD: look", Tick);

    private bool _hiding;

    /// Hiding is on. The windows are looked at only from a <see cref="Trigger"/> until <see cref="Timing.WindowsOsdMatch"/> after it (the
    /// only time a pop-up can be taken); what was on screen at the end of the last look is what the next one compares with.
    public bool Hidden
    {
        get => _hiding;
        set
        {
            if (value == _hiding) return;
            if (!value)
            {
                _hiding = false;
                _timer.Stop();
                Restore();
                return;
            }
            if (_stopped) return;
            _hiding = true;
            PutBackEarlierRun();
            _openedAt.Clear();
            _onScreen = WindowsOsdWindows.Find().Where(WindowsOsdWindows.IsOnScreen).ToHashSet();
        }
    }

    /// A change Windows may show its pop-up for happened just now (volume, brightness, microphone or media, made outside OmniControl). Any thread.
    public void Trigger()
    {
        Interlocked.Exchange(ref _lastChange, Environment.TickCount64);
        Dispatcher.UIThread.Post(StartLooking);
    }

    private void StartLooking()
    {
        if (!_hiding) return;
        if (_flyout != IntPtr.Zero)
        {
            // A minimised host never presents the pop-up: no look needed.
            if (WindowsOsdWindows.IsMinimised(_flyout)) return;
            if (!_unminimisedLogged)
            {
                _unminimisedLogged = true;
                Log($"Windows un-minimised the pop-up's host; the event hook ({_guard.Parked} caught) and the fast look take over, and it is minimised again");
            }
            if (WindowsOsdWindows.IsParked(_flyout)) WindowsOsdWindows.Minimise(_flyout);
        }
        // A known pop-up costs one window check per look, so it is checked often; learning one enumerates windows, so it keeps the slower rate.
        _timer.Interval = _flyout != IntPtr.Zero ? Timing.WindowsOsdFastPoll : Timing.WindowsOsdPoll;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Tick()
    {
        Look();
        // The match window is over: nothing can be taken for the pop-up until the next change.
        if (Environment.TickCount64 - Interlocked.Read(ref _lastChange) > Timing.WindowsOsdMatch.TotalMilliseconds) _timer.Stop();
    }

    private void Look()
    {
        // The change is read before the clock, so a change heard during this look can never give a negative gap.
        var lastChange = Interlocked.Read(ref _lastChange);
        var now = Environment.TickCount64;
        var gap = now - lastChange;
        var afterChange = gap >= 0 && gap <= Timing.WindowsOsdMatch.TotalMilliseconds;

        if (_flyout != IntPtr.Zero && !WindowsOsdWindows.IsSameCandidate(_flyout, _flyoutProcess))
        {
            Log($"forgot 0x{_flyout.ToInt64():x}: it is gone or no longer the same kind of window");
            Forget();
        }

        var candidates = _flyout != IntPtr.Zero ? new List<IntPtr> { _flyout } : WindowsOsdWindows.Find();
        var onScreen = candidates.Where(WindowsOsdWindows.IsOnScreen).ToHashSet();
        foreach (var opened in onScreen.Where(w => !_onScreen.Contains(w))) _openedAt[opened] = now;
        foreach (var closed in _openedAt.Keys.Where(w => !onScreen.Contains(w)).ToList()) _openedAt.Remove(closed);
        _onScreen = onScreen;

        foreach (var window in onScreen.Where(w => _openedAt.TryGetValue(w, out var at) && at == now))
            Opened(window, afterChange, gap);
    }

    /// A candidate just opened (it was not on screen at the last look).
    private void Opened(IntPtr window, bool afterChange, long gap)
    {
        var known = window == _flyout;
        if (!afterChange)
        {
            if (known)
            {
                Log($"let go of {WindowsOsdWindows.Describe(window)}: Windows showed it with no outside change before it");
                Forget();
            }
            return;
        }
        if (!WindowsOsdWindows.HasFlyoutShape(window))
        {
            Log($"left alone {WindowsOsdWindows.Describe(window)}: opened {gap} ms after a change, but it is not the pop-up's size or place");
            if (known) Forget();
            return;
        }
        if (!known)
        {
            if (WindowsOsdWindows.PointerOverShell()) return; // a taskbar pop-up opening under the pointer, never the volume pop-up
            if (!Learn(window)) return;
        }
        if (known && _guard.Hooked && !_missLogged)
        {
            _missLogged = true;
            Log($"the event hook missed Windows showing the pop-up ({_guard.Parked} caught by it since learned); the look caught it");
        }
        Park(window, $"opened {gap} ms after a change");
    }

    /// Takes the window for the pop-up; false (and hiding stopped for the run) when too many different windows were taken.
    private bool Learn(IntPtr window)
    {
        _taken.Add(window);
        if (_taken.Count > Timing.WindowsOsdMaxWindows)
        {
            Log($"stopped hiding for this run: {_taken.Count} different windows looked like Windows' pop-up, so they are not being told apart");
            _stopped = true;
            _hiding = false;
            _timer.Stop();
            Restore();
            return false;
        }
        _flyout = window;
        _flyoutProcess = WindowsOsdWindows.ProcessOf(window);
        _guard.Watch(window, _flyoutProcess);
        Log($"learned {WindowsOsdWindows.Describe(window)} as Windows' pop-up");
        return true;
    }

    private void Park(IntPtr window, string why)
    {
        if (WindowsOsdWindows.PlaceOf(window) is not { } place) return;
        _parkedFrom = place;
        AppDataStore.Save(DataStores.WindowsOsdHidden, new WindowsOsdRecord
        {
            Window = window.ToInt64(), ProcessId = _flyoutProcess, Left = place.Left, Top = place.Top, Width = place.Width, Height = place.Height,
            Minimised = true,
        });
        Log($"moving off screen and minimising {WindowsOsdWindows.Describe(window)} ({why})");
        WindowsOsdWindows.Park(window);
        WindowsOsdWindows.Minimise(window); // queued after the move, so its restore place is off screen
    }

    /// Puts the pop-up back where it was if it is still off screen, and forgets it.
    private void Forget()
    {
        _guard.Stop(); // before any put-back, so the hook never parks it again
        if (_parkedFrom is { } place)
        {
            if (_flyout != IntPtr.Zero && WindowsOsdWindows.IsParked(_flyout))
            {
                if (WindowsOsdWindows.IsMinimised(_flyout)) WindowsOsdWindows.Unminimise(_flyout);
                WindowsOsdWindows.MoveTo(_flyout, place);
                Log($"put back 0x{_flyout.ToInt64():x} at {place.Left},{place.Top}");
            }
            AppDataStore.Delete(DataStores.WindowsOsdHidden);
        }
        _flyout = IntPtr.Zero;
        _flyoutProcess = 0;
        _parkedFrom = null;
    }

    /// The pop-up an earlier run moved off screen (and could not put back, as it ended first) is put back, only when that handle is still
    /// the same pop-up of the same process and still where OmniControl parked it. Nothing else is touched.
    private void PutBackEarlierRun()
    {
        if (AppDataStore.Load<WindowsOsdRecord>(DataStores.WindowsOsdHidden) is not { } record) return;
        AppDataStore.Delete(DataStores.WindowsOsdHidden);
        var window = new IntPtr(record.Window);
        if (!WindowsOsdWindows.IsSameCandidate(window, record.ProcessId) || !WindowsOsdWindows.IsParked(window))
        {
            Log($"an earlier run's record of 0x{record.Window:x} no longer matches a parked pop-up; nothing touched");
            return;
        }
        if (record.Minimised && WindowsOsdWindows.IsMinimised(window)) WindowsOsdWindows.Unminimise(window);
        WindowsOsdWindows.MoveTo(window, new WindowPlace(record.Left, record.Top, record.Width, record.Height));
        Log($"put back 0x{record.Window:x} at {record.Left},{record.Top}, left off screen by an earlier run");
    }

    /// Brings back Windows' own pop-up: the one this run moved off screen, and one an earlier run left off screen.
    public void Restore()
    {
        Forget();
        PutBackEarlierRun();
        _openedAt.Clear();
        _onScreen.Clear();
    }

    private void Log(string line)
    {
        if (_logged++ > MaxLogLines) return;
        AppDataStore.AppendLog(LogFiles.Osd, line);
    }

    public void Dispose() => Hidden = false;
}
