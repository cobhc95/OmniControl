namespace OmniControl.Core.Engine.Resources;

/// The sensor part of a sample (GPU load, disk activity, temperatures, the Hottest sensor): Windows' own sources first
/// (<see cref="WindowsSensors"/>); LibreHardwareMonitor (<see cref="LibreSensors"/>) for the CPU temperature (its package over an ACPI thermal
/// zone, until it shows it has no CPU sensor), for a reading Windows gives none of now (a graphics card or drive whose driver reports no
/// temperature, no GPU or disk counters), for a chosen sensor of its own, and for the sensor menu while it is open (the board's chips are only its).
/// The CPU and GPU temperature on Auto are picked by <see cref="TemperaturePick"/>.
/// Windows' sources are read at most every <see cref="CoreTiming.SensorEvery"/>, the temperatures every <see cref="CoreTiming.TemperatureEvery"/>,
/// the drives' every <see cref="CoreTiming.StorageSensorEvery"/> while a disk reading is shown, else <see cref="CoreTiming.StorageTempEvery"/>;
/// the library on the same clocks. One worker thread only.
internal sealed class SensorReader : IDisposable
{
    private const SampleParts SensorBits = SampleParts.Sensors | SampleParts.SensorList;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly WindowsSensors _windows = new();
    private LibreSensors? _libre; // made at the first reading Windows cannot give: only then is the library's assembly loaded
    private TimeSpan? _sensedAt, _drivesAt, _tempsAt; // null: read at the next sample
    private (SampleParts Wanted, SensorChoices Choices) _last;
    private double? _gpuLoad;
    private IReadOnlyDictionary<int, double>? _activity;
    private List<TemperatureSensor> _windowsTemps = new(), _driveTemps = new();
    private LibreReading _libreRead = LibreReading.None;
    private bool _libreDrivesNoHelp; // the library had no temperature either for a drive Windows gives none for: not opened for that again until a disk change
    private bool _libreCpuNoHelp; // the library read no CPU sensor (no PawnIO driver): the thermal zone is the CPU temperature until the sources close
    private readonly Dictionary<string, SensorGroup> _chosen = new(); // each chosen sensor of the library and the group it was last found in
    private volatile bool _rescan;

    /// Why the sensor library could not start (null: it runs, is not needed, or has not been asked yet).
    public string? Problem => _libre?.Problem;

    public ResourceSample Add(ResourceSample sample, SampleParts wanted, SensorChoices choices)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var now = _clock.Elapsed;
        if (_rescan)
        {
            _rescan = false;
            _windows.RescanDisks();
            _libre?.RescanStorage();
            (_libreDrivesNoHelp, _drivesAt) = (false, null);
        }
        if ((wanted & SensorBits, choices) != _last)
        {
            _last = (wanted & SensorBits, choices);
            (_sensedAt, _drivesAt, _tempsAt) = (null, null, null); // a reading just asked for is read at once
        }
        if (_sensedAt is not { } sensed || now - sensed >= CoreTiming.SensorEvery)
        {
            _sensedAt = now;
            var tempsDue = Due(ref _tempsAt, now, CoreTiming.TemperatureEvery);
            var drivesDue = Due(ref _drivesAt, now, Any(SampleParts.Disk | SampleParts.DiskTemp) ? CoreTiming.StorageSensorEvery : CoreTiming.StorageTempEvery);
            Sense(wanted, choices, tempsDue, drivesDue);
        }

        var all = _windowsTemps.Concat(_driveTemps).Concat(_libreRead.Temperatures).ToList();
        TemperatureSensor? Find(SampleParts part, string? id)
        {
            var found = id is null ? null : all.FirstOrDefault(s => s.Id == id);
            if (Any(part) && id is not null && !IsWindows(id))
            {
                // Remembered so the next reads open only that sensor's group; a chosen sensor that is gone (not one only out of range now) opens every group again.
                if (found is not null) _chosen[id] = found.Group;
                else if (_libre?.Has(id) != true) _chosen.Remove(id);
            }
            return found;
        }
        foreach (var id in _chosen.Keys.Except(choices.Ids).ToList()) _chosen.Remove(id);
        var hot = Any(SampleParts.Hottest) ? Find(SampleParts.Hottest, choices.Hottest) ?? all.MaxBy(s => s.Value) : null;
        var cpu = Any(SampleParts.CpuTemp) ? Find(SampleParts.CpuTemp, choices.Cpu) ?? TemperaturePick.Cpu(all.Where(IsCpuCandidate)) : null;
        var gpu = Any(SampleParts.GpuTemp)
            ? Find(SampleParts.GpuTemp, choices.Gpu) ?? TemperaturePick.Gpu(_windowsTemps.Where(t => t.Group == SensorGroup.Gpu))
                ?? TemperaturePick.Gpu(_libreRead.Temperatures.Where(t => t.Group == SensorGroup.Gpu))
            : null;
        return sample with
        {
            Gpu = Any(SampleParts.Gpu) ? _gpuLoad ?? _libreRead.Gpu : null,
            Disk = Any(SampleParts.Disk) ? _activity is { } activity ? (activity.Count > 0 ? activity.Values.Max() : null) : _libreRead.Disk : null,
            Disks = Any(SampleParts.Disk | SampleParts.DiskTemp)
                ? Disks(Any(SampleParts.Disk), Any(SampleParts.DiskTemp), choices.Disks, id => Find(SampleParts.DiskTemp, id))
                : Array.Empty<DiskSample>(),
            CpuTemp = cpu?.Value,
            GpuTemp = gpu?.Value,
            Hottest = hot?.Value,
            HottestName = hot?.Hardware,
            HottestSensor = hot,
            CpuTempSensor = cpu,
            GpuTempSensor = gpu,
            TemperatureSensors = Any(SampleParts.SensorList) ? all.OrderBy(t => t.Group).ToList() : Array.Empty<TemperatureSensor>(),
        };
    }

    /// The CPU's own sensors (the library's) and the ACPI thermal zones, its fallback.
    private static bool IsCpuCandidate(TemperatureSensor s) => s.Group == SensorGroup.Cpu || s.Id.StartsWith(WindowsSensors.ZoneIds, StringComparison.Ordinal);

    private static bool Due(ref TimeSpan? at, TimeSpan now, TimeSpan every)
    {
        if (at is { } last && now - last < every) return false;
        at = now;
        return true;
    }

    private static bool IsWindows(string? sensor) => sensor?.StartsWith(WindowsSensors.IdPrefix, StringComparison.Ordinal) == true;

    /// The chosen sensors of the readings wanted now.
    private static IEnumerable<string> Chosen(SampleParts wanted, SensorChoices choices)
    {
        if ((wanted & SampleParts.Hottest) != 0 && choices.Hottest is { } hottest) yield return hottest;
        if ((wanted & SampleParts.CpuTemp) != 0 && choices.Cpu is { } cpu) yield return cpu;
        if ((wanted & SampleParts.GpuTemp) != 0 && choices.Gpu is { } gpu) yield return gpu;
        if ((wanted & SampleParts.DiskTemp) != 0)
            foreach (var disk in choices.Disks.Values) yield return disk;
    }

    private void Sense(SampleParts wanted, SensorChoices choices, bool tempsDue, bool drivesDue)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var hottest = Any(SampleParts.Hottest | SampleParts.SensorList);
        var chosen = Chosen(wanted, choices).ToList();
        bool Chose(string ids) => chosen.Any(id => id.StartsWith(ids, StringComparison.Ordinal));
        _gpuLoad = Any(SampleParts.Gpu) ? _windows.GpuLoad() : null;
        _activity = Any(SampleParts.Disk) ? _windows.DiskActivity() : null;
        if (tempsDue)
            _windowsTemps = _windows.Temperatures(zones: Any(SampleParts.CpuTemp) || hottest || Chose(WindowsSensors.ZoneIds),
                gpus: Any(SampleParts.GpuTemp) || hottest || Chose(WindowsSensors.GpuIds), drives: false);
        if (drivesDue)
            _driveTemps = Any(SampleParts.DiskTemp) || hottest || Chose(WindowsSensors.DiskIds) ? _windows.Temperatures(zones: false, gpus: false, drives: true) : new();

        var groups = LibreGroupsFor(wanted, choices);
        if (!groups.Any)
        {
            _libre?.Close();
            _libreRead = LibreReading.None;
            return;
        }
        var libre = _libre ??= new LibreSensors();
        if (libre.Use(groups) is not { } fresh)
        {
            _libreRead = LibreReading.None;
            return;
        }
        _libreRead = libre.Read(readStorage: drivesDue || fresh, readTemps: tempsDue || fresh, gpuLoad: Any(SampleParts.Gpu) && _gpuLoad is null);
        if (groups.Cpu && (tempsDue || fresh) && !_libreRead.Temperatures.Any(t => t.Group == SensorGroup.Cpu)) _libreCpuNoHelp = true;
        var missing = _windows.DisksWithoutTemperature;
        if (groups.Storage && (drivesDue || fresh) && missing.Count > 0 && !_libreRead.Disks.Any(d => missing.Contains(d.Number) && d.Temperature is not null))
            _libreDrivesNoHelp = true;
    }

    /// The library's groups a read needs, from what Windows gave in this read.
    private LibreGroups LibreGroupsFor(SampleParts wanted, SensorChoices choices)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        // Auto: nothing chosen, or a chosen Windows sensor that is gone (a chosen sensor of the library opens its own group below).
        bool Auto(string? id) => id is null || (IsWindows(id) && !_windowsTemps.Concat(_driveTemps).Any(t => t.Id == id));
        var zones = _windowsTemps.Any(t => t.Group == SensorGroup.Board);
        var gpuTemps = _windowsTemps.Any(t => t.Group == SensorGroup.Gpu);
        var drivesMissing = !_libreDrivesNoHelp && _windows.DisksWithoutTemperature.Count > 0;
        var groups = new LibreGroups(
            Cpu: Any(SampleParts.CpuTemp) && Auto(choices.Cpu) && !_libreCpuNoHelp,
            Gpu: (Any(SampleParts.Gpu) && _gpuLoad is null) || (Any(SampleParts.GpuTemp) && Auto(choices.Gpu) && !gpuTemps),
            Storage: (Any(SampleParts.Disk) && _activity is null) || (Any(SampleParts.DiskTemp) && drivesMissing),
            Board: false);
        if (Any(SampleParts.SensorList)) groups |= new LibreGroups(true, !gpuTemps, drivesMissing, true);
        foreach (var id in Chosen(wanted, choices).Where(id => !IsWindows(id)))
            groups |= _chosen.TryGetValue(id, out var group) ? LibreGroups.Of(group) : new LibreGroups(true, true, true, true);
        // The Hottest sensor on Auto: Windows' sensors, and the library's only for a group Windows has none in.
        if (Any(SampleParts.Hottest) && Auto(choices.Hottest)) groups |= new LibreGroups(!zones, !gpuTemps, drivesMissing, !zones);
        return groups;
    }

    /// Each physical disk's activity and temperature: Windows' own, else the library's; a disk's chosen sensor (by its number, looked up by
    /// <paramref name="find"/>) gives its temperature while it is found.
    private IReadOnlyList<DiskSample> Disks(bool activity, bool temperature, IReadOnlyDictionary<int, string> chosen, Func<string, TemperatureSensor?> find)
    {
        var numbers = (_activity?.Keys ?? Enumerable.Empty<int>()).Concat(_driveTemps.Select(t => t.DiskNumber).OfType<int>())
            .Concat(_libreRead.Disks.Select(d => d.Number)).Concat(temperature ? chosen.Keys : Enumerable.Empty<int>()).Distinct().Order();
        return numbers.Select(n =>
        {
            double? active = _activity is { } a ? (a.TryGetValue(n, out var v) ? v : null) : _libreRead.Disks.FirstOrDefault(d => d.Number == n)?.Activity;
            var hot = !temperature ? null : (chosen.TryGetValue(n, out var id) ? find(id) : null) ?? _driveTemps.FirstOrDefault(t => t.DiskNumber == n)
                ?? _libreRead.Temperatures.Where(t => t.DiskNumber == n).MaxBy(t => t.Value);
            return new DiskSample(n, activity ? active : null, hot?.Value) { Sensor = hot };
        }).ToList();
    }

    /// A disk was added or removed (any thread): the next reads list the drives again.
    public void RescanStorage() => _rescan = true;

    /// Gives back what the sources hold (the library's memory and driver handles, the counters); the next read that needs one opens it again.
    public void Close()
    {
        _windows.Dispose();
        _libre?.Close();
        (_windowsTemps, _driveTemps, _libreRead, _gpuLoad, _activity, _libreCpuNoHelp) = (new(), new(), LibreReading.None, null, null, false);
        (_sensedAt, _drivesAt, _tempsAt) = (null, null, null);
    }

    public void Dispose() => Close();
}
