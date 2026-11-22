using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources;

/// THE poller of the readings: one worker timer at the shortest interval needed, reading only the parts asked for, and stopped when nobody needs it.
/// It owns the live list of readings, the graphics adapters' and physical disks' included (<see cref="RefreshHardware"/>, through the hardware watches).
public sealed class ResourceMonitor : IReadingList, IDisposable
{
    public const string PageNeed = "page", GadgetNeed = "gadget";
    private const string RulesNeed = "rules";

    private readonly IStateRegistry _states;
    private readonly Func<TimeSpan> _ruleInterval;
    private readonly Func<string?> _adapterChoice;
    private readonly Func<IReadOnlyDictionary<string, string>> _sensorChoices, _gpuChoices, _speedChoices;
    private readonly string _category;
    private readonly Dictionary<string, (TimeSpan Every, SampleParts Parts)> _needs = new();
    private readonly Dictionary<string, Queue<double>> _history = new();
    private volatile IReadOnlyList<PhysicalDisk>? _disks; // as last listed (worker); null until the first look
    private volatile IReadOnlyList<GraphicsAdapter>? _gpus; // the same for the graphics adapters
    private readonly Dictionary<string, (double Low, double High)> _today = new(); // each reading's lowest and highest read since midnight (memory only)
    private DateTime _day = DateTime.Today;
    private readonly object _work = new(); // one read at a time, and none while disposing
    private readonly ResourceSampler _sampler = new();
    private readonly Timer _timer, _idleClose;
    private volatile SampleParts _wanted;
    private bool _disposed, _sensorsWanted;
    private (IReadOnlyDictionary<string, string> Sensors, IReadOnlyDictionary<string, string> Gpus, IReadOnlyDictionary<string, string> Speeds) _chosen =
        (new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>()); // each reading's sensor and adapter at the last read
    private int _listing, _listingAdapters; // sensor menus open, network adapter menus open

    /// <param name="ruleInterval">How often to read while only a rule follows a reading (the page's interval).</param>
    /// <param name="adapterChoice">The network adapter the speeds are read from (the setup's choice), asked at every read.</param>
    /// <param name="sensorChoices">The sensor each temperature reading follows, by reading id (the setup's one map, never changed in place), asked at every read.</param>
    /// <param name="gpuChoices">The graphics adapter each GPU reading follows, by reading id (the same), asked at every read.</param>
    /// <param name="speedChoices">The network adapter each of Download and Upload follows on its own, by reading id (the same), asked at every read.</param>
    /// <param name="states">Where the readings are offered as states.</param>
    public ResourceMonitor(IStateRegistry states, Func<TimeSpan> ruleInterval, Func<string?> adapterChoice, Func<IReadOnlyDictionary<string, string>> sensorChoices,
        Func<IReadOnlyDictionary<string, string>> gpuChoices, Func<IReadOnlyDictionary<string, string>> speedChoices, string category)
    {
        (_states, _ruleInterval, _adapterChoice, _sensorChoices, _gpuChoices, _speedChoices, _category) =
            (states, ruleInterval, adapterChoice, sensorChoices, gpuChoices, speedChoices, category);
        _timer = new Timer(_ => Tick());
        _idleClose = new Timer(_ => Release());
        states.Register(Reading.All.Select(StateOf));
        states.WatchedChanged += FollowRules;
        FollowRules();
    }

    private StateEntry StateOf(Reading r) => new(r.StateId, r.Name, _category, StateKind.Level, () => r.InUnit(Latest) is { } v ? StateValue.Of(Math.Round(v), DateTime.Now) : null,
        "Read by OmniControl's Resources poller while the page, the taskbar gadget or a rule needs it.", Max: r.Max, Unit: r.Unit, Icon: r.Icon, Keywords: "resources usage load monitor");

    public IReadOnlyList<Reading> Readings { get; private set; } = Reading.All;

    public Reading? Find(string id) => Readings.FirstOrDefault(r => r.Id == id);

    public event Action? ReadingsChanged;

    private IReadOnlyList<Reading> HardwareReadings => Readings.Skip(Reading.All.Count).ToList();

    public IReadOnlyList<GraphicsAdapter> Gpus { get; private set; } = Array.Empty<GraphicsAdapter>();

    /// True once the physical disks and graphics adapters were listed (the hardware watch's stale check: the first look at start-up).
    public bool HardwareKnown => _disks is not null && _gpus is not null;

    /// The disks and graphics adapters as last listed, for the hardware log.
    public string HardwareText() => string.Join(", ", (_gpus?.Select(g => g.Name) ?? Array.Empty<string>())
        .Concat(_disks?.Select(d => $"Disk {d.Number} {d.Model}") ?? Array.Empty<string>()));

    /// Lists the physical disks and graphics adapters again (the hardware watch's refresh: start-up, a device added or removed, a wake); a
    /// changed list makes the sensor sources list them again and their readings and states follow. Worker.
    public void RefreshHardware()
    {
        var (disks, gpus) = (PhysicalDisks.List(), GraphicsAdapters.List());
        if (_disks is { } before && before.SequenceEqual(disks) && _gpus is { } gpusBefore && gpusBefore.SequenceEqual(gpus)) return;
        (_disks, _gpus) = (disks, gpus);
        _sampler.Rescan();
        Dispatcher.UIThread.Post(() => TakeHardware(disks, gpus));
    }

    private void TakeHardware(IReadOnlyList<PhysicalDisk> disks, IReadOnlyList<GraphicsAdapter> gpus)
    {
        if (_disposed) return;
        var before = HardwareReadings;
        Gpus = gpus;
        // One graphics adapter is the built-in GPU readings themselves.
        var eachGpu = gpus.Count > 1 ? gpus.SelectMany((g, i) => Reading.OfGpu(g, i + 1)) : Enumerable.Empty<Reading>();
        Readings = Reading.All.Concat(eachGpu).Concat(disks.SelectMany(Reading.OfDisk)).ToList();
        var left = before.Where(r => Find(r.Id) is null).ToList();
        foreach (var reading in left)
        {
            _history.Remove(reading.Id);
            _today.Remove(reading.Id);
        }
        _states.Unregister(left.Select(r => r.StateId));
        _states.Register(HardwareReadings.Select(StateOf)); // a renumbered disk's states are replaced: their names say its number
        ReadingsChanged?.Invoke();
        FollowRules();
    }

    /// The latest reading (UI thread).
    public ResourceSample Latest { get; private set; } = ResourceSample.None;

    /// Why the sensors cannot be read (one sentence), or null.
    public string? SensorProblem => _sampler.SensorProblem;

    /// A read arrived (UI thread).
    public event Action? Updated;

    /// The recent values of a reading in its unit, oldest first (NaN where it was not known).
    public IReadOnlyList<double> History(string readingId) => _history.TryGetValue(readingId, out var history) ? history.ToList() : Array.Empty<double>();

    /// The lowest and highest value of a reading read since midnight (as <see cref="Reading.Read"/> gives them), or null when none was read today. UI thread.
    public (double Low, double High)? Today(string readingId) => _day == DateTime.Today && _today.TryGetValue(readingId, out var range) ? range : null;

    /// Says that <paramref name="who"/> needs these <paramref name="parts"/> every <paramref name="every"/> (null: no longer). UI thread.
    public void Need(string who, TimeSpan? every, SampleParts parts = SampleParts.None)
    {
        if (every is { } interval) _needs[who] = (interval, parts);
        else _needs.Remove(who);
        Apply();
    }

    /// A sensor menu opened (true) or closed: while one is open every read lists all temperature sensors (at once, so the menu fills). UI thread.
    public void ListSensors(bool open)
    {
        _listing = Math.Max(0, _listing + (open ? 1 : -1));
        Apply();
    }

    /// A network adapter menu opened (true) or closed: while one is open every read lists the adapters (the network is read for it). UI thread.
    public void ListAdapters(bool open)
    {
        _listingAdapters = Math.Max(0, _listingAdapters + (open ? 1 : -1));
        Apply();
    }

    private void Apply()
    {
        _wanted = _needs.Values.Aggregate(_listing > 0 ? SampleParts.SensorList : SampleParts.None, (all, need) => all | need.Parts);
        if (_listingAdapters > 0) _wanted |= SampleParts.Network | SampleParts.Adapters;
        var sensors = (_wanted & SampleParts.Sensors) != 0;
        if (sensors != _sensorsWanted)
        {
            _sensorsWanted = sensors;
            _idleClose.Change(sensors ? Timeout.InfiniteTimeSpan : Timing.ResourcesIdleClose, Timeout.InfiniteTimeSpan);
        }
        var shortest = _needs.Count == 0 ? (TimeSpan?)null : _needs.Values.Min(n => n.Every);
        _timer.Change(shortest is null ? Timeout.InfiniteTimeSpan : TimeSpan.Zero, shortest ?? Timeout.InfiniteTimeSpan);
    }

    /// Gives back the sensor sources (the sensor library too) when no sensor reading was wanted for a while. Worker; waits for a read.
    private void Release()
    {
        lock (_work)
        {
            if (_disposed) return;
            Guard.Run("Resources release", _sampler.CloseSensors);
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
                var chosen = (_sensorChoices(), _gpuChoices(), _speedChoices());
                var sample = _sampler.Read(_wanted, _adapterChoice(), ChoicesOf(chosen.Item1, chosen.Item2),
                    new SpeedAdapters(chosen.Item3.GetValueOrDefault(Reading.DownloadId), chosen.Item3.GetValueOrDefault(Reading.UploadId)));
                Dispatcher.UIThread.Post(() => Arrived(sample, chosen));
            });
        }
        finally { Monitor.Exit(_work); }
    }

    /// The readings' choices as the sampler takes them: each disk's by its number now, each adapter's by its id (worker).
    private SensorChoices ChoicesOf(IReadOnlyDictionary<string, string> chosen, IReadOnlyDictionary<string, string> gpus) => new(chosen.GetValueOrDefault(Reading.HottestId),
        chosen.GetValueOrDefault(Reading.CpuTempId), chosen.GetValueOrDefault(Reading.GpuTempId),
        (_disks ?? Array.Empty<PhysicalDisk>()).Where(d => chosen.ContainsKey(Reading.TemperatureIdOf(d))).ToDictionary(d => d.Number, d => chosen[Reading.TemperatureIdOf(d)]))
    {
        Gpus = (_gpus ?? Array.Empty<GraphicsAdapter>()).Where(g => chosen.ContainsKey(Reading.TemperatureIdOf(g))).ToDictionary(g => g.Id, g => chosen[Reading.TemperatureIdOf(g)]),
        UsageGpu = gpus.GetValueOrDefault(Reading.GpuId),
        TemperatureGpu = gpus.GetValueOrDefault(Reading.GpuTempId),
    };

    private void Arrived(ResourceSample sample,
        (IReadOnlyDictionary<string, string> Sensors, IReadOnlyDictionary<string, string> Gpus, IReadOnlyDictionary<string, string> Speeds) chosen)
    {
        if (_disposed) return;
        Latest = sample;
        // Another sensor or adapter is another line: its reading's graph and today's range start afresh.
        foreach (var id in Changed(chosen.Sensors, _chosen.Sensors).Concat(Changed(chosen.Gpus, _chosen.Gpus)).Concat(Changed(chosen.Speeds, _chosen.Speeds)).ToList()) Forget(id);
        _chosen = chosen;
        if (_day != DateTime.Today)
        {
            _day = DateTime.Today;
            _today.Clear();
        }
        var read = new List<string>();
        foreach (var reading in Readings.Where(r => (sample.Parts & r.Part) == r.Part)) // a part nobody asked for leaves its history and state alone
        {
            if (!_history.TryGetValue(reading.Id, out var history)) _history[reading.Id] = history = new Queue<double>();
            history.Enqueue(reading.InUnit(sample) ?? double.NaN);
            while (history.Count > Timing.ResourcesHistory) history.Dequeue();
            if (reading.Read(sample) is { } value)
                _today[reading.Id] = _today.TryGetValue(reading.Id, out var range) ? (Math.Min(range.Low, value), Math.Max(range.High, value)) : (value, value);
            read.Add(reading.StateId);
        }
        _states.Notify(read); // one look for every reading of this sample
        Updated?.Invoke();
    }

    private static IEnumerable<string> Changed(IReadOnlyDictionary<string, string> now, IReadOnlyDictionary<string, string> before) =>
        now.Keys.Union(before.Keys).Where(id => now.GetValueOrDefault(id) != before.GetValueOrDefault(id));

    private void Forget(string readingId)
    {
        _history.Remove(readingId);
        _today.Remove(readingId);
    }

    public void Dispose()
    {
        _states.WatchedChanged -= FollowRules;
        _timer.Dispose();
        _idleClose.Dispose();
        lock (_work)
        {
            _disposed = true;
            _sampler.Dispose();
        }
        _states.Unregister(Readings.Select(r => r.StateId));
    }
}
