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
/// <param name="LooksStale">A cheap check: true when what the program shows no longer matches the hardware. Used by the periodic safety check and,
/// after an announcement, by the retry schedule (<see cref="HardwareRetry"/>), which stops once it is false. Null: one refresh per burst of announcements.</param>
public sealed record HardwareWatch(string Id, string Name, HardwareSignal Signals, Action Refresh, Func<bool>? LooksStale = null);

/// <summary>
/// THE master list of hardware watches and the one place that decides when hardware is looked at again. Three ways in, all ending in the
/// same refresh of a watch: Windows' announcements (<see cref="HardwareSignals"/> and the sound devices' own, passed in through
/// <see cref="Signal"/>), bursts joined, then one refresh per watch or, for a watch that can tell when it looks stale, the retry schedule
/// <see cref="Timing.HardwareRetrySchedule"/>; an action whose picked device is missing (<see cref="RefreshThen"/>); and
/// an optional periodic check (<see cref="SetCheck"/>) that refreshes only the watches that say they look stale. Every refresh runs on a
/// worker thread inside <see cref="Guard"/>. Each announcement and each scheduled try is one line in <see cref="LogFile"/>.
/// </summary>
public sealed class HardwareWatches : IDisposable
{
    private sealed class Slot
    {
        public required HardwareWatch Watch { get; init; }
        public required ThrottledRun Run { get; init; }
        public HardwareRetry? Retry { get; init; }
        public DateTime LastOnDemand { get; set; } = DateTime.MinValue;
    }

    /// <summary>The hardware diagnostics log: announcements and what each refresh found.</summary>
    public const string LogFile = "hardware-log.txt";

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
        var slot = new Slot
        {
            Watch = watch,
            Run = new ThrottledRun(Timing.HardwareRefreshGap, watch.Refresh),
            Retry = watch.LooksStale is { } stale ? new HardwareRetry(watch.Name, _retrySchedule, watch.Refresh, stale, _log) : null,
        };
        Slot? old;
        lock (_gate)
        {
            if (_disposed) { Release(slot); return; }
            old = _slots.FirstOrDefault(s => s.Watch.Id == watch.Id);
            if (old is not null) _slots.Remove(old);
            _slots.Add(slot);
        }
        if (old is not null) Release(old);
    }

    /// <summary>Starts listening to Windows. Never in a screenshot export (<see cref="StartupMode.Inert"/>).</summary>
    public void Start()
    {
        if (StartupMode.Inert || _signals is not null) return;
        _signals = new HardwareSignals(Timing.PowerClockTick, Timing.PowerClockGap);
        _signals.Raised += Signal;
        _signals.Power += OnPower;
        _signals.Failed += text => AppDataStore.AppendLog(Guard.LogFile, $"Hardware signals: {text}");
        _log("Listening for hardware announcements.");
        _signals.Start(); // when Windows refuses the window the reason is in the log; on-demand and periodic refreshes still work
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
        foreach (var slot in hit)
        {
            if (slot.Retry is { } retry) retry.Start();
            else slot.Run.Request(Timing.HardwareDebounce);
        }
    }

    /// <summary>A watch's own feature noticed a change (a touchpad plugged in): that watch alone is refreshed after the same settle time.</summary>
    public void Request(string id)
    {
        lock (_gate) _slots.FirstOrDefault(s => s.Watch.Id == id)?.Run.Request(Timing.HardwareDebounce);
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
            Guard.Run($"Hardware refresh {slot.Watch.Name}", slot.Watch.Refresh);
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

    /// <summary>Timer thread: refreshes the watches whose cheap check says they look stale (a watch without a check is left to signals).</summary>
    private void CheckStale()
    {
        List<Slot> slots;
        lock (_gate) slots = _slots.Where(s => s.Watch.LooksStale is not null).ToList();
        foreach (var slot in slots)
        {
            var stale = false;
            if (Guard.Run($"Hardware check {slot.Watch.Name}", () => stale = slot.Watch.LooksStale!()) is null && stale) slot.Run.Request();
        }
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
        foreach (var slot in slots) Release(slot);
    }

    private void OnPower(PowerSignal signal) => Power?.Invoke(signal);

    private static void Release(Slot slot)
    {
        slot.Run.Dispose();
        slot.Retry?.Dispose();
    }
}
