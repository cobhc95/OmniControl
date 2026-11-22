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
    private bool? _libreFile; // the library is there (Setup may leave it out), looked at once
    private LibreSensors? _libre; // made at the first reading Windows cannot give: only then is the library's assembly loaded
    private TimeSpan? _sensedAt, _drivesAt, _tempsAt; // null: read at the next sample
    private (SampleParts Wanted, SensorChoices Choices) _last;
    private IReadOnlyDictionary<string, double>? _gpuLoads; // by adapter LUID; null: no GPU counters, or not wanted
    private IReadOnlyList<GraphicsAdapter>? _gpuList; // listed at the first read that needs the adapters apart
    private IReadOnlyDictionary<int, double>? _activity;
    private List<TemperatureSensor> _windowsTemps = new(), _driveTemps = new();
    private LibreReading _libreRead = LibreReading.None;
    private bool _libreDrivesNoHelp; // the library had no temperature either for a drive Windows gives none for: not opened for that again until a disk change
    private bool _libreGpusNoHelp; // the library had no temperature either for an adapter Windows gives none for: not opened for that again until a hardware change
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
            (_libreDrivesNoHelp, _libreGpusNoHelp, _gpuList, _drivesAt) = (false, false, null, null);
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
        TemperatureSensor? GpuTemp(GraphicsAdapter adapter) =>
            (choices.Gpus.TryGetValue(adapter.Id, out var id) ? Find(SampleParts.GpuTemp, id) : null) ?? OwnTemperature(adapter);
        GpuSample GpuNow(GraphicsAdapter adapter) => Any(SampleParts.GpuTemp) && GpuTemp(adapter) is { } t
            ? new GpuSample(adapter.Id, Any(SampleParts.Gpu) ? LoadOf(adapter) : null, t.Value) { Sensor = t }
            : new GpuSample(adapter.Id, Any(SampleParts.Gpu) ? LoadOf(adapter) : null, null);
        var usageGpu = Any(SampleParts.Gpu) ? GpuOf(choices.UsageGpu) : null;
        var gpu = Any(SampleParts.GpuTemp)
            ? Find(SampleParts.GpuTemp, choices.Gpu) ?? (GpuOf(choices.TemperatureGpu) is { } chosenGpu ? OwnTemperature(chosenGpu)
                : TemperaturePick.Gpu(_windowsTemps.Where(t => t.Group == SensorGroup.Gpu)) ?? TemperaturePick.Gpu(_libreRead.Temperatures.Where(t => t.Group == SensorGroup.Gpu)))
            : null;
        return sample with
        {
            Gpu = Any(SampleParts.Gpu) ? usageGpu is not null ? LoadOf(usageGpu) : _gpuLoads is { } loads ? loads.Values.DefaultIfEmpty(0).Max() : _libreRead.Gpu : null,
            Gpus = Any(SampleParts.EachGpu) ? GpuList().Select(GpuNow).ToList() : Array.Empty<GpuSample>(),
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

    private IReadOnlyList<GraphicsAdapter> GpuList() => _gpuList ??= GraphicsAdapters.List();

    /// The chosen adapter while it is there; null on Auto or when it is gone.
    private GraphicsAdapter? GpuOf(string? id) => id is null ? null : GpuList().FirstOrDefault(a => a.Id == id);

    private double? LoadOf(GraphicsAdapter adapter) => _gpuLoads is { } loads ? loads.GetValueOrDefault(adapter.Luid) : null;

    /// The adapter's own temperature: Windows' (the driver's), else the best of the library's sensors on hardware of its name.
    private TemperatureSensor? OwnTemperature(GraphicsAdapter adapter) => _windowsTemps.FirstOrDefault(t => t.GpuLuid == adapter.Luid)
        ?? TemperaturePick.Gpu(_libreRead.Temperatures.Where(t => t.Group == SensorGroup.Gpu && SameHardware(t.Hardware, adapter.Name)));

    /// The library and Windows may name one card slightly apart ("AMD Radeon(TM) Graphics", "AMD Radeon Graphics").
    private static bool SameHardware(string a, string b) => Plain(a) == Plain(b);

    private static string Plain(string name) => new(name.Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// The adapters whose temperature is wanted now: all while each is read apart, else the GPU temperature's chosen adapter.
    private IEnumerable<GraphicsAdapter> GpusWanted(SampleParts wanted, SensorChoices choices) =>
        (wanted & SampleParts.GpuTemp) == 0 ? Enumerable.Empty<GraphicsAdapter>()
        : (wanted & SampleParts.EachGpu) != 0 ? GpuList()
        : GpuOf(choices.TemperatureGpu) is { } one ? new[] { one } : Enumerable.Empty<GraphicsAdapter>();

    /// The wanted adapters Windows gives no temperature for.
    private IEnumerable<GraphicsAdapter> GpusMissing(SampleParts wanted, SensorChoices choices) =>
        GpusWanted(wanted, choices).Where(a => _windowsTemps.All(t => t.GpuLuid != a.Luid));

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
        if ((wanted & (SampleParts.GpuTemp | SampleParts.EachGpu)) == (SampleParts.GpuTemp | SampleParts.EachGpu))
            foreach (var sensor in choices.Gpus.Values) yield return sensor;
    }

    private void Sense(SampleParts wanted, SensorChoices choices, bool tempsDue, bool drivesDue)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var hottest = Any(SampleParts.Hottest | SampleParts.SensorList);
        var chosen = Chosen(wanted, choices).ToList();
        bool Chose(string ids) => chosen.Any(id => id.StartsWith(ids, StringComparison.Ordinal));
        _gpuLoads = Any(SampleParts.Gpu) ? _windows.GpuLoads() : null;
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
        if (!(_libreFile ??= OptionalFiles.Present(OptionalFiles.Sensors)))
        {
            _libreRead = LibreReading.None; // left out by Setup: Windows' own sensors only
            return;
        }
        var libre = _libre ??= new LibreSensors();
        if (libre.Use(groups) is not { } fresh)
        {
            _libreRead = LibreReading.None;
            return;
        }
        _libreRead = libre.Read(readStorage: drivesDue || fresh, readTemps: tempsDue || fresh, gpuLoad: Any(SampleParts.Gpu) && _gpuLoads is null);
        if (groups.Cpu && (tempsDue || fresh) && !_libreRead.Temperatures.Any(t => t.Group == SensorGroup.Cpu)) _libreCpuNoHelp = true;
        var missing = _windows.DisksWithoutTemperature;
        if (groups.Storage && (drivesDue || fresh) && missing.Count > 0 && !_libreRead.Disks.Any(d => missing.Contains(d.Number) && d.Temperature is not null))
            _libreDrivesNoHelp = true;
        var gpusMissing = GpusMissing(wanted, choices).ToList();
        if (groups.Gpu && (tempsDue || fresh) && gpusMissing.Count > 0
            && !_libreRead.Temperatures.Any(t => t.Group == SensorGroup.Gpu && gpusMissing.Any(a => SameHardware(t.Hardware, a.Name))))
            _libreGpusNoHelp = true;
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
            Gpu: (Any(SampleParts.Gpu) && _gpuLoads is null) || (Any(SampleParts.GpuTemp) && Auto(choices.Gpu) && !gpuTemps)
                || (Any(SampleParts.GpuTemp) && !_libreGpusNoHelp && GpusMissing(wanted, choices).Any()),
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

    /// A disk or graphics adapter was added or removed (any thread): the next reads list them again.
    public void Rescan() => _rescan = true;

    /// Gives back what the sources hold (the library's memory and driver handles, the counters); the next read that needs one opens it again.
    public void Close()
    {
        _windows.Dispose();
        _libre?.Close();
        (_windowsTemps, _driveTemps, _libreRead, _gpuLoads, _activity, _libreCpuNoHelp, _libreGpusNoHelp, _gpuList) = (new(), new(), LibreReading.None, null, null, false, false, null);
        (_sensedAt, _drivesAt, _tempsAt) = (null, null, null);
    }

    public void Dispose() => Close();
}
