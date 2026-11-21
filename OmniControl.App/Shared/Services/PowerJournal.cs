using System.Diagnostics;

namespace OmniControl.App.Shared.Services;

/// The two power facts the automations start from: a confirmed wake, and whether this start-up is a new log-in.
public interface IPowerEvents
{
    /// The PC really woke (confirmed by two channels, or Windows' own suspend-and-resume pair); raised once per wake, on any thread.
    event Action? Woke;

    /// This PC is about to sleep, hibernate or shut down (on the listener's thread, while there is still a moment).
    event Action<PowerEventKind>? Leaving;

    /// A power action's cancellable countdown on this PC started (<see cref="PowerCountdown.Left"/> its length), ran out (zero) or was cancelled or failed (null).
    event Action<PowerCountdown?>? Countdown;

    /// Says a change of the countdown (the power guard, the one place that runs it).
    void Counting(PowerCountdown? now);

    /// True once per sign-in when OmniControl was started by its start-up entry; false for a second start in the same sign-in.
    bool TakeLogin(bool startedByWindows);
}

/// A power action's countdown: the action's id and the time left.
public sealed record PowerCountdown(string ActionId, TimeSpan Left);

/// One line of the power journal: what happened, when, and which channels told it.
public sealed class PowerJournalEntry
{
    public DateTime At { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Channels { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// What <c>power-journal.json</c> holds: the latest entries, how far the event log was read, and the sign-in "At log in" last ran for.
public sealed class PowerJournalData
{
    public List<PowerJournalEntry> Entries { get; set; } = new();
    public DateTime? EventLogUpTo { get; set; }
    public DateTime? LoginBoot { get; set; }
    public int LoginSession { get; set; } = -1;
}

/// THE power journal: every power and session event the hardware listeners hear (<see cref="HardwareWatches.Power"/>; no listener of its own)
/// goes in <c>power-journal.json</c> (the same kind told by several channels within <see cref="Timing.PowerEventSame"/> is one entry) and in
/// <see cref="LogFile"/>, and the <see cref="WakeDetector"/> decides from them when the PC really woke (each decision is a log line). At
/// start-up it reads the journal, the time since Windows started and the System event log to say what happened while OmniControl was not
/// running (logged only: no rule runs for the past). Thread-safe: listeners call it from their own threads.
public sealed class PowerJournal : IPowerEvents, IDisposable
{
    /// The power diagnostics log: every event, every wake decision.
    public const string LogFile = LogFiles.Power;

    private const int Kept = 300, EventLogMax = 50;

    private readonly PowerJournalData _data;
    private readonly WakeDetector _detector;
    private readonly Action<PowerJournalData> _save;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _now;
    private readonly Func<long> _ms;
    private readonly Func<DateTime> _boot;
    private readonly int _session;
    private readonly object _gate = new();
    private readonly object _saveGate = new(); // taken before _gate, never inside it
    private readonly IHardwareWatches? _hardware;
    private Timer? _expire;

    public event Action? Woke;

    /// This PC is about to sleep, hibernate or shut down (the event that says so, on the listener's thread, while there is still a moment): other paired PCs are told so their tag is exact.
    public event Action<PowerEventKind>? Leaving;

    public event Action<PowerCountdown?>? Countdown;

    public void Counting(PowerCountdown? now) => Countdown?.Invoke(now);

    /// The app's journal: listens to <paramref name="hardware"/>'s power events, saved in <see cref="DataStores.PowerJournal"/>.
    public PowerJournal(IHardwareWatches hardware)
        : this(AppDataStore.Load<PowerJournalData>(DataStores.PowerJournal) ?? new(), data => AppDataStore.Save(DataStores.PowerJournal, data),
            line => AppDataStore.AppendLog(LogFile, line), () => DateTime.Now, () => Environment.TickCount64,
            () => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64), OwnSession())
    {
        _hardware = hardware;
        hardware.Power += Record;
    }

    private static int OwnSession()
    {
        using var self = Process.GetCurrentProcess();
        return self.SessionId;
    }

    /// A journal with its time, store and log given (tests replay signals with a fake clock).
    internal PowerJournal(PowerJournalData data, Action<PowerJournalData> save, Action<string> log, Func<DateTime> now, Func<long> ms, Func<DateTime> boot, int session)
    {
        _data = data;
        _save = save;
        _log = log;
        _now = now;
        _ms = ms;
        _boot = boot;
        _session = session;
        _detector = new WakeDetector((long)Timing.WakeConfirmWindow.TotalMilliseconds, (long)Timing.WakeQuiet.TotalMilliseconds,
            (long)Timing.StandbyDisplayOffMin.TotalMilliseconds);
    }

    /// Makes the confirm timer (it runs once, after a power event: <see cref="Timing.WakeDecideAfter"/>) and, off the UI thread, the look
    /// at what happened while OmniControl was not running.
    public void Start()
    {
        if (_expire is not null) return;
        _expire = new Timer(_ => Guard.Run("Power journal: wake check", Tick), null, Timeout.Infinite, Timeout.Infinite);
        Task.Run(() => Guard.Run("Power journal: start-up look", LookBack));
    }

    /// One event from a listener (any thread): journalled, logged and given to the wake detector.
    public void Record(PowerSignal signal)
    {
        WakeDecision? decision;
        lock (_gate)
        {
            Add(signal, _now());
            decision = _detector.Observe(signal, _ms());
        }
        _log($"{signal.Kind} ({signal.Channel}: {signal.Detail})");
        if (signal.Kind is PowerEventKind.Suspend or PowerEventKind.Hibernate or PowerEventKind.Shutdown && Leaving is { } leaving)
            foreach (var handler in leaving.GetInvocationList().Cast<Action<PowerEventKind>>())
                Guard.Run("Power journal: leaving handler", () => handler(signal.Kind));
        Decided(decision);
        _expire?.Change(Timing.WakeDecideAfter, Timeout.InfiniteTimeSpan); // a candidate left waiting is decided once its window passed
        if (signal.Kind is PowerEventKind.Shutdown or PowerEventKind.Logoff) Save(); // the process may end before a queued save runs
        else Task.Run(() => Guard.Run("Power journal: save", Save));
    }

    /// Timer: a wake candidate whose confirm window passed is decided.
    internal void Tick()
    {
        WakeDecision? decision;
        lock (_gate) decision = _detector.Expire(_ms());
        Decided(decision);
    }

    public bool TakeLogin(bool startedByWindows)
    {
        if (!startedByWindows) return false;
        var boot = _boot();
        lock (_gate)
        {
            if (_data.LoginSession == _session && _data.LoginBoot is { } last && (boot - last).Duration() < Timing.LoginSameBoot)
            {
                _log($"At log in: not run again, it already ran for this sign-in (Windows started {boot:g}); OmniControl was started again.");
                return false;
            }
            _data.LoginBoot = boot;
            _data.LoginSession = _session;
            Add(new PowerSignal(PowerEventKind.Logon, PowerChannel.Uptime, "OmniControl started by its start-up entry"), _now());
        }
        _log($"At log in: a new sign-in (Windows started {boot:g}, session {_session}).");
        Save();
        return true;
    }

    /// The journal's entries, oldest first (a copy).
    public IReadOnlyList<PowerJournalEntry> Entries
    {
        get { lock (_gate) return _data.Entries.ToList(); }
    }

    private void Decided(WakeDecision? decision)
    {
        if (decision is null) return;
        _log($"Wake {decision.Verdict.ToString().ToLowerInvariant()}: {decision.Why} [{string.Join(", ", decision.Channels)}].");
        if (decision.Verdict != WakeVerdict.Confirmed) return;
        if (Woke is { } woke)
            foreach (var handler in woke.GetInvocationList().Cast<Action>())
                Guard.Run("Power journal: wake handler", handler);
        if (_expire is not null) Task.Run(() => Guard.Run("Power journal: event log after a wake", () => ReadEventLog(running: true)));
    }

    /// The same kind within <see cref="Timing.PowerEventSame"/> of the last entry is that entry told again: its channel is added.
    private void Add(PowerSignal signal, DateTime at)
    {
        var kind = signal.Kind.ToString();
        var channel = signal.Channel.ToString();
        if (_data.Entries.LastOrDefault() is { } last && last.Kind == kind && (at - last.At).Duration() < Timing.PowerEventSame)
        {
            if (!last.Channels.Split(", ").Contains(channel)) last.Channels += $", {channel}";
            return;
        }
        _data.Entries.Add(new PowerJournalEntry { At = at, Kind = kind, Channels = channel, Detail = signal.Detail });
        if (_data.Entries.Count > Kept) _data.Entries.RemoveRange(0, _data.Entries.Count - Kept);
    }

    /// One writer at a time: several listeners (a wake is told by three channels at once), the start-up look and the log-in check all save,
    /// and two saves of one file at once collided on its temp file. The copy is taken inside the writer's lock, so the last save is the newest.
    private void Save()
    {
        lock (_saveGate)
        {
            PowerJournalData copy;
            lock (_gate)
                copy = new PowerJournalData
                {
                    Entries = _data.Entries.Select(e => new PowerJournalEntry { At = e.At, Kind = e.Kind, Channels = e.Channels, Detail = e.Detail }).ToList(),
                    EventLogUpTo = _data.EventLogUpTo, LoginBoot = _data.LoginBoot, LoginSession = _data.LoginSession,
                };
            _save(copy);
        }
    }

    /// Start-up: what happened while OmniControl was not running, from the journal, the time since Windows started and the event log.
    internal void LookBack()
    {
        var boot = _boot();
        PowerJournalEntry? last;
        lock (_gate) last = _data.Entries.LastOrDefault();
        if (last is null) _log("Power journal started (no earlier entries).");
        else if (boot > last.At)
        {
            lock (_gate) Add(new PowerSignal(PowerEventKind.Boot, PowerChannel.Uptime, $"Windows started {boot:g}"), boot);
            _log($"Windows started {boot:g}, after the last entry ({last.Kind} {last.At:g}): it restarted while OmniControl was not running.");
        }
        else if (last.Kind is nameof(PowerEventKind.Suspend) or nameof(PowerEventKind.Hibernate))
            _log($"The last entry is {last.Kind} {last.At:g} with no restart since: the PC woke while OmniControl was not running (it was closed after the wake).");
        ReadEventLog(running: false);
        Save();
    }

    /// Adds the sleeps and wakes Windows logged since the last read; at start-up they are the past, so they are only logged.
    private void ReadEventLog(bool running)
    {
        DateTime since;
        lock (_gate) since = _data.EventLogUpTo ?? _boot();
        var (events, problem) = PowerEventLog.Since(since, EventLogMax);
        if (problem is not null)
        {
            _log($"Event log: {problem}.");
            return;
        }
        lock (_gate)
        {
            foreach (var logged in events) Add(logged.Signal, logged.At);
            if (events.Count > 0) _data.EventLogUpTo = events[^1].At;
            _data.Entries.Sort((a, b) => a.At.CompareTo(b.At));
        }
        foreach (var logged in events)
            _log($"Event log: {logged.Signal.Kind} {logged.At:g} ({logged.Signal.Detail}){(running ? string.Empty : ", while OmniControl was not running: no rule runs for it")}.");
        if (running) Save();
    }

    public void Dispose()
    {
        if (_hardware is not null) _hardware.Power -= Record;
        _expire?.Dispose();
        _expire = null;
    }
}
