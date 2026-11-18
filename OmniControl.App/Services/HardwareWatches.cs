using OmniControl.Core.Engine;

namespace OmniControl.App.Services;

/// <summary>
/// One thing that depends on hardware and can go stale (the monitors, the sound devices, the touchpad...). Its owner registers it once in
/// <see cref="HardwareWatches"/>.
/// </summary>
/// <param name="Id">Stable name other code refers to ("displays.hardware"); an action that picks devices names it in <c>HotkeyAction.HardwareWatch</c>.</param>
/// <param name="Name">What the watch is called in the log.</param>
/// <param name="Signals">Which Windows announcements can affect it (the one place that says which signal refreshes what).</param>
/// <param name="Refresh">Looks at the hardware again. Runs on a worker thread and returns when done; one that must touch the window waits for the UI thread itself.</param>
/// <param name="LooksStale">A cheap check: true when what the program shows no longer matches the hardware (something changed, or answers less
/// than it did). Used by the periodic safety check and, after an announcement, by the retry schedule (<see cref="HardwareRetry"/>), which stops
/// once it is false. Null: one refresh per burst of announcements.</param>
/// <param name="LooksUnsettled">A stricter check for the retry schedule after an announcement and at start, when the hardware may still be
/// waking (a monitor not answering at all): the schedule goes on while it is true. Null: <paramref name="LooksStale"/>. Never used by the
/// periodic check, so hardware that never answers is not refreshed again and again.</param>
/// <param name="Describe">What the watch holds now, in a few words for the log line of each automatic refresh ("3 displays, 3 DDC/CI monitors: ...").</param>
public sealed record HardwareWatch(string Id, string Name, HardwareSignal Signals, Action Refresh, Func<bool>? LooksStale = null,
    Func<bool>? LooksUnsettled = null, Func<string>? Describe = null)
{
    /// <summary>" Found: ..." for a log line, or empty when the watch cannot describe itself (a failure to describe is logged, never thrown).</summary>
    internal string Found()
    {
        string? text = null;
        if (Describe is { } describe) Guard.Run($"Hardware describe {Name}", () => text = describe());
        return string.IsNullOrWhiteSpace(text) ? string.Empty : $" Found: {text}.";
    }
}

/// <summary>
/// THE master list of hardware watches and the one place that decides when hardware is looked at again. Five ways in, all ending in the
/// same refresh of a watch, each refresh one line in <see cref="LogFile"/> with what started it and what it found:
/// Windows' announcements (<see cref="HardwareSignals"/> and the sound devices' own, passed in through <see cref="Signal"/>; the screens
/// coming back on and a clock gap, from the same listeners' power events), bursts joined, then one refresh per watch or, for a watch that can
/// tell when it looks stale, the retry schedule <see cref="Timing.HardwareRetrySchedule"/>; the start of the program (the same schedule, each
/// try only when the watch looks wrong); a watch's own feature (<see cref="Request"/>); an action whose picked device is missing
/// (<see cref="RefreshThen"/>); and the periodic check (<see cref="SetCheck"/>) that refreshes only the watches that say they look stale,
/// at most <see cref="Timing.HardwareStaleRefreshMax"/> times in a row before it waits for an announcement. Every refresh runs on a worker
/// thread inside <see cref="Guard"/>.
/// </summary>
public sealed class HardwareWatches : IDisposable
{
    private sealed class Slot
    {
        private string? _trigger;
        private int _staleRuns;

        public Slot(HardwareWatch watch, Action<Slot> work, HardwareRetry? retry)
        {
            Watch = watch;
            Retry = retry;
            Run = new ThrottledRun(Timing.HardwareRefreshGap, () => work(this));
        }

        public HardwareWatch Watch { get; }
        public ThrottledRun Run { get; }
        public HardwareRetry? Retry { get; }
        public DateTime LastOnDemand { get; set; } = DateTime.MinValue;

        /// <summary>Asks for a throttled refresh; <paramref name="trigger"/> is what the log line will say started it (the latest of a joined burst).</summary>
        public void Request(string trigger, TimeSpan? delay)
        {
            Volatile.Write(ref _trigger, trigger);
            Run.Request(delay);
        }

        public string TakeTrigger() => Interlocked.Exchange(ref _trigger, null) ?? "requested";

        /// <summary>How many periodic refreshes in a row left the watch still looking stale; an announcement starts the count again.</summary>
        public int StaleRuns => Volatile.Read(ref _staleRuns);
        public int CountStaleRun() => Interlocked.Increment(ref _staleRuns);
        public void ResetStaleRuns() => Volatile.Write(ref _staleRuns, 0);

        public void Dispose()
        {
            Run.Dispose();
            Retry?.Dispose();
        }
    }

    /// <summary>The hardware diagnostics log: announcements and what each refresh found.</summary>
    public const string LogFile = "hardware-log.txt";

    private const string StartTrigger = "start", PeriodicTrigger = "periodic check", ActionTrigger = "an action's device was missing", OwnTrigger = "its own notice";

    private readonly List<Slot> _slots = new();
    private readonly IReadOnlyList<TimeSpan> _retrySchedule;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private HardwareSignals? _signals;
    private Timer? _periodic;
    private bool _disposed;

    /// <param name="retrySchedule">When a stale-checking watch is tried after an announcement; <see cref="Timing.HardwareRetrySchedule"/> unless a test sets it.</param>
    /// <param name="log">Where the diagnostics lines go; <see cref="LogFile"/> unless a test listens.</param>
    public HardwareWatches(IReadOnlyList<TimeSpan>? retrySchedule = null, Action<string>? log = null)
    {
        _retrySchedule = retrySchedule ?? Timing.HardwareRetrySchedule;
        _log = log ?? (line => AppDataStore.AppendLog(LogFile, line));
    }

    /// <summary>
    /// Every power and session event the same listeners hear (sleep, wake, lock, log on, display off and on, clock gaps), with the channel
    /// that told it; raised on a listener's thread. The power journal (<see cref="PowerJournal"/>) is the one that listens.
    /// </summary>
    public event Action<PowerSignal>? Power;

    /// <summary>The watches, in the order they were registered.</summary>
    public IReadOnlyList<HardwareWatch> Watches
    {
        get { lock (_gate) return _slots.Select(s => s.Watch).ToList(); }
    }

    /// <summary>Adds a watch; the same id twice replaces the first.</summary>
    public void Register(HardwareWatch watch)
    {
        var slot = new Slot(watch, RefreshLogged, watch.LooksStale is not null ? new HardwareRetry(watch, _retrySchedule, _log) : null);
        Slot? old;
        lock (_gate)
        {
            if (_disposed) { slot.Dispose(); return; }
            old = _slots.FirstOrDefault(s => s.Watch.Id == watch.Id);
            if (old is not null) _slots.Remove(old);
            _slots.Add(slot);
        }
        old?.Dispose();
    }

    /// <summary>
    /// Starts listening to Windows and gives every watch that can tell when it looks wrong its retry schedule once, each try only when it
    /// does (at log-on the monitors may not answer yet when the program first reads them). Never in a screenshot export (<see cref="StartupMode.Inert"/>).
    /// </summary>
    public void Start()
    {
        if (StartupMode.Inert || _signals is not null) return;
        _signals = new HardwareSignals(Timing.PowerClockTick, Timing.PowerClockGap);
        _signals.Raised += Signal;
        _signals.Power += OnPower;
        _signals.Failed += text => AppDataStore.AppendLog(Guard.LogFile, $"Hardware signals: {text}");
        _log("Listening for hardware announcements.");
        _signals.Start(); // when Windows refuses the window the reason is in the log; on-demand and periodic refreshes still work
        List<Slot> slots;
        lock (_gate) slots = _slots.ToList();
        foreach (var slot in slots) slot.Retry?.Start(StartTrigger, checkFirst: true);
    }

    /// <summary>
    /// A Windows announcement arrived (any thread): every watch that lists it is refreshed once its burst has settled, a watch that can tell
    /// when it looks stale on its retry schedule. Also how a feature's own watcher (the mixer's sound-device notice) joins in.
    /// </summary>
    /// <param name="source">Which listener told it, for the log line.</param>
    public void Signal(HardwareSignal signal, string? source = null)
    {
        List<Slot> hit;
        lock (_gate) hit = _slots.Where(s => (s.Watch.Signals & signal) != 0).ToList();
        _log($"Signal {signal}{(source is null ? "" : $" ({source})")}: {(hit.Count == 0 ? "no watch" : string.Join(", ", hit.Select(s => s.Watch.Name)))}.");
        var trigger = $"after {signal}";
        foreach (var slot in hit)
        {
            slot.ResetStaleRuns();
            // The screens coming back on leaves the monitors' handles valid: only a watch that looks wrong is refreshed.
            if (slot.Retry is { } retry) retry.Start(trigger, checkFirst: signal == HardwareSignal.DisplayPower);
            else slot.Request(trigger, Timing.HardwareDebounce);
        }
    }

    /// <summary>A watch's own feature noticed a change (a touchpad plugged in): that watch alone is refreshed after the same settle time.</summary>
    public void Request(string id)
    {
        Slot? slot;
        lock (_gate) slot = _slots.FirstOrDefault(s => s.Watch.Id == id);
        slot?.Request(OwnTrigger, Timing.HardwareDebounce);
    }

    /// <summary>
    /// The one on-demand path: refreshes the watch now (worker thread), then calls <paramref name="then"/> (on that worker, whatever the
    /// outcome), so an action can try again before it says a device is missing. False, with nothing done, when there is no such watch or it
    /// was refreshed for an action less than <see cref="Timing.HardwareOnDemandGap"/> ago.
    /// </summary>
    public bool RefreshThen(string id, Action then)
    {
        Slot? slot;
        lock (_gate)
        {
            slot = _slots.FirstOrDefault(s => s.Watch.Id == id);
            if (_disposed || slot is null || Grace.Within(slot.LastOnDemand, Timing.HardwareOnDemandGap)) return false;
            slot.LastOnDemand = DateTime.UtcNow;
        }
        Task.Run(() =>
        {
            Refresh(slot, ActionTrigger);
            Guard.Run($"Hardware retry after {slot.Watch.Name}", then);
        });
        return true;
    }

    /// <summary>The setting: how often the periodic safety check runs (<see cref="HardwareCheck.Off"/> stops it). Saved in <see cref="AppSettings"/>.</summary>
    public HardwareCheck Check => AppSettings.Current.HardwareCheck;

    /// <summary>Chooses how often the periodic check runs and applies it at once.</summary>
    public void SetCheck(HardwareCheck value)
    {
        var settings = AppSettings.Current;
        if (settings.HardwareCheck != value)
        {
            settings.HardwareCheck = value;
            settings.Save();
        }
        ApplyCheck();
    }

    /// <summary>Starts the periodic check as saved (call once the watches are registered), or stops it when Off.</summary>
    public void ApplyCheck()
    {
        Timer? old;
        lock (_gate)
        {
            if (_disposed) return;
            old = _periodic;
            _periodic = null;
            if (!StartupMode.Inert && HardwareChecks.EveryOf(Check) is { } every)
                _periodic = new Timer(_ => CheckStale(), null, every, every);
        }
        old?.Dispose();
    }

    /// <summary>
    /// Timer thread: asks each watch's cheap check (no device is talked to beyond what it already knows) and refreshes the ones that look
    /// stale. A watch still stale after <see cref="Timing.HardwareStaleRefreshMax"/> such refreshes in a row (a monitor switched to another
    /// input) is left alone until the next announcement, so the page is not rebuilt every interval for nothing.
    /// </summary>
    private void CheckStale()
    {
        List<Slot> slots;
        lock (_gate) slots = _slots.Where(s => s.Watch.LooksStale is not null).ToList();
        foreach (var slot in slots)
        {
            var stale = false;
            if (Guard.Run($"Hardware check {slot.Watch.Name}", () => stale = slot.Watch.LooksStale!()) is not null) continue;
            if (!stale) { slot.ResetStaleRuns(); continue; }
            var runs = slot.CountStaleRun();
            if (runs <= Timing.HardwareStaleRefreshMax) slot.Request(PeriodicTrigger, null);
            else if (runs == Timing.HardwareStaleRefreshMax + 1)
                _log($"{slot.Watch.Name} ({PeriodicTrigger}): still looks stale after {Timing.HardwareStaleRefreshMax} refreshes; waiting for an announcement or Refresh.{slot.Watch.Found()}");
        }
    }

    /// <summary>The throttled refresh of a watch (worker thread), told in the log with what asked for it.</summary>
    private void RefreshLogged(Slot slot) => Refresh(slot, slot.TakeTrigger());

    /// <summary>Refreshes one watch now (worker thread) and writes one log line: what started it, the result, and what the watch found.</summary>
    private void Refresh(Slot slot, string trigger)
    {
        var watch = slot.Watch;
        var failure = Guard.Run($"Hardware refresh {watch.Name}", watch.Refresh);
        string result;
        if (failure is not null) result = $"refresh failed ({failure.GetType().Name}: {failure.Message}).";
        else if (watch.LooksStale is not { } looksStale) result = "refreshed.";
        else
        {
            var stale = true;
            Guard.Run($"Hardware check {watch.Name}", () => stale = looksStale());
            result = stale ? "refreshed; still looks stale." : "refreshed; looks right.";
        }
        _log($"{watch.Name} ({trigger}): {result}{watch.Found()}");
    }

    public void Dispose()
    {
        List<Slot> slots;
        Timer? periodic;
        lock (_gate)
        {
            _disposed = true;
            slots = _slots.ToList();
            _slots.Clear();
            periodic = _periodic;
            _periodic = null;
        }
        if (_signals is { } signals)
        {
            signals.Raised -= Signal;
            signals.Power -= OnPower;
            signals.Dispose();
            _signals = null;
        }
        periodic?.Dispose();
        foreach (var slot in slots) slot.Dispose();
    }

    /// <summary>
    /// A power event (listener thread): passed on to the power journal, and two of them are also hardware announcements the window does not
    /// make itself: the screens coming back on (monitors that slept may not have answered meanwhile) and a clock gap (a wake that sent no message).
    /// </summary>
    private void OnPower(PowerSignal signal)
    {
        if (signal.Kind == PowerEventKind.DisplayOn) Signal(HardwareSignal.DisplayPower, signal.Detail);
        else if (signal.Kind == PowerEventKind.ClockGap) Signal(HardwareSignal.Resume, $"clock gap, {signal.Detail}");
        Power?.Invoke(signal);
    }
}
