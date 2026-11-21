namespace OmniControl.Core.Engine.Resources;

/// The sensor part of a sample (GPU load, disk activity, temperatures, the Hottest sensor): Windows' own sources first
/// (<see cref="WindowsSensors"/>); LibreHardwareMonitor (<see cref="LibreSensors"/>) only for a reading Windows gives none of now (the CPU
/// temperature on a PC without an ACPI thermal zone, a graphics card or drive whose driver reports no temperature, no GPU or disk counters),
/// for a chosen Hottest sensor of its own, and for the sensor menu while it is open (the CPU package and the board's chips are only its).
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
    private (SampleParts Wanted, string? Sensor) _last;
    private double? _gpuLoad;
    private IReadOnlyDictionary<int, double>? _activity;
    private List<TemperatureSensor> _windowsTemps = new(), _driveTemps = new();
    private LibreReading _libreRead = LibreReading.None;
    private bool _libreDrivesNoHelp; // the library had no temperature either for a drive Windows gives none for: not opened for that again until a disk change
    private (string Id, SensorGroup Group)? _chosen; // a chosen sensor of the library and the group it was last found in
    private volatile bool _rescan;

    /// Why the sensor library could not start (null: it runs, is not needed, or has not been asked yet).
    public string? Problem => _libre?.Problem;

    public ResourceSample Add(ResourceSample sample, SampleParts wanted, string? sensor)
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
        if ((wanted & SensorBits, sensor) != _last)
        {
            _last = (wanted & SensorBits, sensor);
            (_sensedAt, _drivesAt, _tempsAt) = (null, null, null); // a reading just asked for is read at once
        }
        if (_sensedAt is not { } sensed || now - sensed >= CoreTiming.SensorEvery)
        {
            _sensedAt = now;
            var tempsDue = Due(ref _tempsAt, now, CoreTiming.TemperatureEvery);
            var drivesDue = Due(ref _drivesAt, now, Any(SampleParts.Disk | SampleParts.DiskTemp) ? CoreTiming.StorageSensorEvery : CoreTiming.StorageTempEvery);
            Sense(wanted, sensor, tempsDue, drivesDue);
        }

        var all = _windowsTemps.Concat(_driveTemps).Concat(_libreRead.Temperatures).ToList();
        var chosen = sensor is null ? null : all.FirstOrDefault(s => s.Id == sensor);
        var hot = chosen ?? all.MaxBy(s => s.Value);
        if (Any(SampleParts.Hottest) && !IsWindows(sensor))
        {
            // Remembered so the next reads open only that sensor's group; a chosen sensor that is gone (not one only out of range now) opens every group again.
            if (chosen is not null) _chosen = (sensor!, chosen.Group);
            else if (sensor is null || _libre?.Has(sensor) != true) _chosen = null;
        }
        double? If(SampleParts part, double? value) => Any(part) ? value : null;
        double? Hottest(SensorGroup group) => _windowsTemps.Where(t => t.Group == group).Select(t => (double?)t.Value).Max();
        return sample with
        {
            Gpu = If(SampleParts.Gpu, _gpuLoad ?? _libreRead.Gpu),
            Disk = If(SampleParts.Disk, _activity is { } activity ? (activity.Count > 0 ? activity.Values.Max() : null) : _libreRead.Disk),
            Disks = Any(SampleParts.Disk | SampleParts.DiskTemp) ? Disks(Any(SampleParts.Disk), Any(SampleParts.DiskTemp)) : Array.Empty<DiskSample>(),
            CpuTemp = If(SampleParts.CpuTemp, Hottest(SensorGroup.Board) ?? _libreRead.CpuTemp),
            GpuTemp = If(SampleParts.GpuTemp, Hottest(SensorGroup.Gpu) ?? _libreRead.GpuTemp),
            Hottest = Any(SampleParts.Hottest) ? hot?.Value : null,
            HottestName = Any(SampleParts.Hottest) ? hot?.Hardware : null,
            ChosenSensor = Any(SampleParts.Hottest) ? chosen?.Name : null,
            TemperatureSensors = Any(SampleParts.SensorList) ? all.OrderBy(t => t.Group).ToList() : Array.Empty<TemperatureSensor>(),
        };
    }

    private static bool Due(ref TimeSpan? at, TimeSpan now, TimeSpan every)
    {
        if (at is { } last && now - last < every) return false;
        at = now;
        return true;
    }

    private static bool IsWindows(string? sensor) => sensor?.StartsWith(WindowsSensors.IdPrefix, StringComparison.Ordinal) == true;

    private void Sense(SampleParts wanted, string? sensor, bool tempsDue, bool drivesDue)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var hottest = Any(SampleParts.Hottest | SampleParts.SensorList);
        _gpuLoad = Any(SampleParts.Gpu) ? _windows.GpuLoad() : null;
        _activity = Any(SampleParts.Disk) ? _windows.DiskActivity() : null;
        if (tempsDue) _windowsTemps = _windows.Temperatures(zones: Any(SampleParts.CpuTemp) || hottest, gpus: Any(SampleParts.GpuTemp) || hottest, drives: false);
        if (drivesDue) _driveTemps = Any(SampleParts.DiskTemp) || hottest ? _windows.Temperatures(zones: false, gpus: false, drives: true) : new();

        var groups = LibreGroupsFor(wanted, sensor);
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
        var missing = _windows.DisksWithoutTemperature;
        if (groups.Storage && (drivesDue || fresh) && missing.Count > 0 && !_libreRead.Disks.Any(d => missing.Contains(d.Number) && d.Temperature is not null))
            _libreDrivesNoHelp = true;
    }

    /// The library's groups a read needs, from what Windows gave in this read.
    private LibreGroups LibreGroupsFor(SampleParts wanted, string? sensor)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var zones = _windowsTemps.Any(t => t.Group == SensorGroup.Board);
        var gpuTemps = _windowsTemps.Any(t => t.Group == SensorGroup.Gpu);
        var drivesMissing = !_libreDrivesNoHelp && _windows.DisksWithoutTemperature.Count > 0;
        var groups = new LibreGroups(
            Cpu: Any(SampleParts.CpuTemp) && !zones,
            Gpu: (Any(SampleParts.Gpu) && _gpuLoad is null) || (Any(SampleParts.GpuTemp) && !gpuTemps),
            Storage: (Any(SampleParts.Disk) && _activity is null) || (Any(SampleParts.DiskTemp) && drivesMissing),
            Board: false);
        if (Any(SampleParts.SensorList)) return groups | new LibreGroups(true, !gpuTemps, drivesMissing, true);
        if (!Any(SampleParts.Hottest)) return groups;
        if (sensor is not null && !IsWindows(sensor)) // one of the library's own sensors was chosen
            return groups | (_chosen is { } c && c.Id == sensor ? LibreGroups.Of(c.Group) : new LibreGroups(true, true, true, true));
        if (sensor is not null && _windowsTemps.Concat(_driveTemps).Any(t => t.Id == sensor)) return groups;
        // Auto (or a chosen Windows sensor that is gone): Windows' sensors, and the library's only for a group Windows has none in.
        return groups | new LibreGroups(!zones, !gpuTemps, drivesMissing, !zones);
    }

    /// Each physical disk's activity and temperature: Windows' own, else the library's.
    private IReadOnlyList<DiskSample> Disks(bool activity, bool temperature)
    {
        var numbers = (_activity?.Keys ?? Enumerable.Empty<int>()).Concat(_driveTemps.Select(t => t.DiskNumber).OfType<int>())
            .Concat(_libreRead.Disks.Select(d => d.Number)).Distinct().Order();
        return numbers.Select(n =>
        {
            var libre = _libreRead.Disks.FirstOrDefault(d => d.Number == n);
            double? active = _activity is { } a ? (a.TryGetValue(n, out var v) ? v : null) : libre?.Activity;
            var hot = _driveTemps.FirstOrDefault(t => t.DiskNumber == n)?.Value ?? libre?.Temperature;
            return new DiskSample(n, activity ? active : null, temperature ? hot : null);
        }).ToList();
    }

    /// A disk was added or removed (any thread): the next reads list the drives again.
    public void RescanStorage() => _rescan = true;

    /// Gives back what the sources hold (the library's memory and driver handles, the counters); the next read that needs one opens it again.
    public void Close()
    {
        _windows.Dispose();
        _libre?.Close();
        (_windowsTemps, _driveTemps, _libreRead, _gpuLoad, _activity) = (new(), new(), LibreReading.None, null, null);
        (_sensedAt, _drivesAt, _tempsAt) = (null, null, null);
    }

    public void Dispose() => Close();
}
