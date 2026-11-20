using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources;

/// THE poller of the readings: one worker timer at the shortest interval needed, reading only the parts asked for, and stopped when nobody needs it.
/// It owns the live list of readings, the physical disks' included (<see cref="RefreshDisks"/>, through the hardware watches).
public sealed class ResourceMonitor : IReadingList, IDisposable
{
    public const string PageNeed = "page", GadgetNeed = "gadget", AppsNeed = "apps";
    private const string RulesNeed = "rules";

    private readonly IStateRegistry _states;
    private readonly Func<TimeSpan> _ruleInterval;
    private readonly Func<string?> _adapterChoice, _sensorChoice;
    private readonly Func<ITrafficSession> _startTraffic;
    private readonly string _category;
    private readonly Dictionary<string, (TimeSpan Every, SampleParts Parts)> _needs = new();
    private readonly Dictionary<string, string> _seen = new(); // program id -> its path, every program seen with traffic this session
    private readonly Dictionary<string, Queue<double>> _history = new();
    private volatile IReadOnlyList<PhysicalDisk>? _disks; // as last listed (worker); null until the first look
    private readonly Dictionary<string, (double Low, double High)> _today = new(); // each reading's lowest and highest read since midnight (memory only)
    private DateTime _day = DateTime.Today;
    private readonly object _work = new(); // one read at a time, and none while disposing
    private readonly ResourceSampler _sampler = new();
    private readonly Timer _timer, _idleClose;
    private ITrafficSession? _apps;
    private volatile bool _wantApps;
    private volatile SampleParts _wanted;
    private bool _disposed, _sensorsWanted;
    private DateTime _lastAppsTake;
    private string? _sensor;

    /// <param name="ruleInterval">How often to read while only a rule follows a reading (the page's interval).</param>
    /// <param name="adapterChoice">The network adapter the speeds are read from (the setup's choice), asked at every read.</param>
    /// <param name="sensorChoice">The temperature sensor the Hottest reading follows (null: the hottest of all), asked at every read.</param>
    /// <param name="startTraffic">Starts the per-program traffic session where the network work runs (a worker; throws an <see cref="IOException"/> saying why).</param>
    public ResourceMonitor(IStateRegistry states, Func<TimeSpan> ruleInterval, Func<string?> adapterChoice, Func<string?> sensorChoice, Func<ITrafficSession> startTraffic, string category)
    {
        (_states, _ruleInterval, _adapterChoice, _sensorChoice, _startTraffic, _category) = (states, ruleInterval, adapterChoice, sensorChoice, startTraffic, category);
        _timer = new Timer(_ => Tick());
        _idleClose = new Timer(_ => Release(sensors: true));
        states.Register(Reading.All.Select(StateOf));
        states.WatchedChanged += FollowRules;
        FollowRules();
    }

    private StateEntry StateOf(Reading r) => new(r.StateId, r.Name, _category, StateKind.Level, () => r.InUnit(Latest) is { } v ? StateValue.Of(Math.Round(v), DateTime.Now) : null,
        "Read by OmniControl's Resources poller while the page, the taskbar gadget or a rule needs it.", Max: r.Max, Unit: r.Unit, Icon: r.Icon, Keywords: "resources usage load monitor");

    public IReadOnlyList<Reading> Readings { get; private set; } = Reading.All;

    public Reading? Find(string id) => Readings.FirstOrDefault(r => r.Id == id);

    public event Action? ReadingsChanged;

    private IReadOnlyList<Reading> DiskReadings => Readings.Skip(Reading.All.Count).ToList();

    /// True once the physical disks were listed (the hardware watch's stale check: the first look at start-up).
    public bool DisksKnown => _disks is not null;

    /// The disks as last listed, for the hardware log.
    public string DisksText() => string.Join(", ", _disks?.Select(d => $"Disk {d.Number} {d.Model}") ?? Array.Empty<string>());

    /// Lists the physical disks again (the hardware watch's refresh: start-up, a device added or removed, a wake); a changed list makes the
    /// sensor library list its drives again and the disks' readings and states follow. Worker.
    public void RefreshDisks()
    {
        var disks = PhysicalDisks.List();
        if (_disks is { } before && before.SequenceEqual(disks)) return;
        _disks = disks;
        _sampler.RescanStorage();
        Dispatcher.UIThread.Post(() => TakeDisks(disks));
    }

    private void TakeDisks(IReadOnlyList<PhysicalDisk> disks)
    {
        if (_disposed) return;
        var before = DiskReadings;
        Readings = Reading.All.Concat(disks.SelectMany(Reading.OfDisk)).ToList();
        var left = before.Where(r => Find(r.Id) is null).ToList();
        foreach (var reading in left)
        {
            _history.Remove(reading.Id);
            _today.Remove(reading.Id);
        }
        _states.Unregister(left.Select(r => r.StateId));
        _states.Register(DiskReadings.Select(StateOf)); // a renumbered disk's states are replaced: their names say its number
        ReadingsChanged?.Invoke();
        FollowRules();
    }

    /// The latest reading (UI thread).
    public ResourceSample Latest { get; private set; } = ResourceSample.None;

    /// The programs' traffic of the latest read in bytes per second, busiest first (empty unless <see cref="AppsNeed"/> is asked).
    public IReadOnlyList<AppTraffic> Apps { get; private set; } = Array.Empty<AppTraffic>();

    /// Why the per-program traffic or the sensors cannot be read (one sentence each), or null.
    public string? AppsProblem { get; private set; }

    public string? SensorProblem => _sampler.SensorProblem;

    /// Every program seen with traffic since OmniControl started (id and path), in the order first seen (UI thread).
    public IReadOnlyDictionary<string, string> Seen => _seen;

    /// A read arrived (UI thread).
    public event Action? Updated;

    /// The recent values of a reading in its unit, oldest first (NaN where it was not known).
    public IReadOnlyList<double> History(string readingId) => _history.TryGetValue(readingId, out var history) ? history.ToList() : Array.Empty<double>();

    /// The lowest and highest value of a reading read since midnight (as <see cref="Reading.Read"/> gives them), or null when none was read today. UI thread.
    public (double Low, double High)? Today(string readingId) => _day == DateTime.Today && _today.TryGetValue(readingId, out var range) ? range : null;

    /// Says that <paramref name="who"/> needs these <paramref name="parts"/> every <paramref name="every"/> (null: no longer); <see cref="AppsNeed"/> reads per program. UI thread.
    public void Need(string who, TimeSpan? every, SampleParts parts = SampleParts.None)
    {
        if (every is { } interval) _needs[who] = (interval, parts);
        else _needs.Remove(who);
        _wantApps = _needs.ContainsKey(AppsNeed);
        if (!_wantApps)
        {
            Apps = Array.Empty<AppTraffic>(); // no speeds of a session that stopped
            if (_apps is not null) ThreadPool.QueueUserWorkItem(_ => Release(sensors: false));
        }
        _wanted = _needs.Values.Aggregate(SampleParts.None, (all, need) => all | need.Parts);
        var sensors = (_wanted & SampleParts.Sensors) != 0;
        if (sensors != _sensorsWanted)
        {
            _sensorsWanted = sensors;
            _idleClose.Change(sensors ? Timeout.InfiniteTimeSpan : Timing.ResourcesIdleClose, Timeout.InfiniteTimeSpan);
        }
        var shortest = _needs.Count == 0 ? (TimeSpan?)null : _needs.Values.Min(n => n.Every);
        _timer.Change(shortest is null ? Timeout.InfiniteTimeSpan : TimeSpan.Zero, shortest ?? Timeout.InfiniteTimeSpan);
    }

    /// Gives back the per-program session when unwanted, and the sensor library when no sensor reading was wanted for a while. Worker; waits for a read.
    private void Release(bool sensors)
    {
        lock (_work)
        {
            if (_disposed) return;
            Guard.Run("Resources release", () =>
            {
                if (!_wantApps)
                {
                    _apps?.Dispose();
                    _apps = null;
                }
                if (sensors) _sampler.CloseSensors();
            });
        }
    }

    private void FollowRules()
    {
        var watched = Readings.Where(r => _states.IsWatched(r.StateId)).ToList();
        Need(RulesNeed, watched.Count > 0 ? _ruleInterval() : null, Reading.PartsOf(watched));
    }

    private void Tick()
    {
        if (!Monitor.TryEnter(_work)) return; // the last read is still going: skip this one
        try
        {
            if (_disposed) return;
            Guard.Run("Resources read", () =>
            {
                var sensor = _sensorChoice();
                var sample = _sampler.Read(_wanted, _adapterChoice(), sensor);
                var apps = ReadApps();
                Dispatcher.UIThread.Post(() => Arrived(sample, apps, sensor));
            });
        }
        finally { Monitor.Exit(_work); }
    }

    /// The programs' bytes per second since the last read, starting or stopping the per-program session as asked (worker).
    private IReadOnlyList<AppTraffic>? ReadApps()
    {
        if (!_wantApps)
        {
            _apps?.Dispose();
            _apps = null;
            return null;
        }
        if (_apps is null)
        {
            try
            {
                _apps = _startTraffic();
                AppsProblem = null;
            }
            catch (IOException ex)
            {
                AppsProblem = ex.Message;
                _wantApps = false;
                return null;
            }
            _lastAppsTake = DateTime.UtcNow;
            return null;
        }
        if (_apps.Stopped is { } why)
        {
            _apps.Dispose();
            _apps = null;
            AppsProblem = why;
            _wantApps = false;
            return null;
        }
        var seconds = Math.Max((DateTime.UtcNow - _lastAppsTake).TotalSeconds, double.Epsilon);
        _lastAppsTake = DateTime.UtcNow;
        return _apps.Take().Select(a => a with { Received = (long)(a.Received / seconds), Sent = (long)(a.Sent / seconds) }).ToList();
    }

    private void Arrived(ResourceSample sample, IReadOnlyList<AppTraffic>? apps, string? sensor)
    {
        if (_disposed) return;
        Latest = sample;
        if (sensor != _sensor)
        {
            _sensor = sensor; // another sensor is another line: its graph and today's range start afresh
            _history.Remove(Reading.HottestId);
            _today.Remove(Reading.HottestId);
        }
        if (apps is not null && _wantApps)
        {
            Apps = apps;
            foreach (var app in apps) _seen.TryAdd(app.Id, app.Path);
        }
        if (_day != DateTime.Today)
        {
            _day = DateTime.Today;
            _today.Clear();
        }
        foreach (var reading in Readings.Where(r => (sample.Parts & r.Part) == r.Part)) // a part nobody asked for leaves its history and state alone
        {
            if (!_history.TryGetValue(reading.Id, out var history)) _history[reading.Id] = history = new Queue<double>();
            history.Enqueue(reading.InUnit(sample) ?? double.NaN);
            while (history.Count > Timing.ResourcesHistory) history.Dequeue();
            if (reading.Read(sample) is { } value)
                _today[reading.Id] = _today.TryGetValue(reading.Id, out var range) ? (Math.Min(range.Low, value), Math.Max(range.High, value)) : (value, value);
            _states.Notify(reading.StateId);
        }
        Updated?.Invoke();
    }

    public void Dispose()
    {
        _states.WatchedChanged -= FollowRules;
        _timer.Dispose();
        _idleClose.Dispose();
        lock (_work)
        {
            _disposed = true;
            _apps?.Dispose();
            _sampler.Dispose();
        }
        _states.Unregister(Readings.Select(r => r.StateId));
    }
}
