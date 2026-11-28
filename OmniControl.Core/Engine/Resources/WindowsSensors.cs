using Microsoft.Win32.SafeHandles;
using static OmniControl.Core.Native.D3dkmtNative;
using static OmniControl.Core.Native.DeviceIoNative;

namespace OmniControl.Core.Engine.Resources;

/// The sensor readings Windows gives itself, without the sensor library: the graphics engines' load and each physical disk's active time
/// (performance counters, as Task Manager), each graphics adapter's temperature (the graphics kernel, as Task Manager), each drive's own
/// temperature (the storage temperature query, mainly NVMe) and the ACPI thermal zones (the board's or the CPU area's sensor that Windows'
/// power management watches). No administrator rights and no driver of OmniControl's. Each source opens at its first read and closes with
/// <see cref="Dispose"/>; one worker thread only.
internal sealed unsafe class WindowsSensors : IDisposable
{
    /// The ids of these sensors start with this, so a chosen Hottest sensor tells which source it needs.
    public const string IdPrefix = "win/";

    /// The id start of each of Windows' sources (a chosen sensor opens only its own).
    public const string ZoneIds = IdPrefix + "zone/", GpuIds = IdPrefix + "gpu/", DiskIds = IdPrefix + "disk/";

    private const uint QueryProperty = 0x2D1400; // IOCTL_STORAGE_QUERY_PROPERTY
    private const int DeviceTemperatureProperty = 52; // StorageDeviceTemperatureProperty
    private const int TemperatureBufferSize = 512, InfoCountAt = 12, InfoAt = 24, InfoSize = 16, InfoTemperatureAt = 2; // STORAGE_TEMPERATURE_DATA_DESCRIPTOR
    private const double Kelvin = 273.15, HottestMax = 150; // a value above this is a misread, not a temperature

    private PdhCounter? _gpu, _disks, _zones;
    private bool _gpuTried, _disksTried, _zonesTried;
    private IReadOnlyList<PhysicalDisk>? _diskList;
    private readonly List<(string Engine, double Sum)> _gpuEngines = new();
    private readonly Dictionary<string, double> _gpuEngineLuids = new(StringComparer.Ordinal);

    /// Each graphics adapter's load in percent as Task Manager gives it (its busiest engine, every program added up), by its LUID
    /// (<see cref="GraphicsAdapter.Luid"/>); null when Windows has no graphics engine counters (no WDDM driver).
    public IReadOnlyDictionary<string, double>? GpuLoads()
    {
        if (Counter(ref _gpu, ref _gpuTried, @"\GPU Engine(*)\Utilization Percentage") is not { } counter) return null;
        // An instance is one program on one engine: "pid_1234_luid_0x00000000_0x0000D1F2_phys_0_eng_3_engtype_3D"; an engine is the part from "luid".
        _gpuEngines.Clear();
        counter.ReadEach((instance, value) =>
        {
            if (value <= 0) return;
            var at = instance.IndexOf("luid_0x".AsSpan(), StringComparison.Ordinal);
            if (at < 0 || instance.Length < at + LuidTextLength) return;
            var engine = instance[at..];
            for (var i = 0; i < _gpuEngines.Count; i++)
            {
                if (engine.SequenceEqual(_gpuEngines[i].Engine.AsSpan()))
                {
                    _gpuEngines[i] = (_gpuEngines[i].Engine, _gpuEngines[i].Sum + value);
                    return;
                }
            }
            _gpuEngines.Add((engine.ToString(), value));
        });
        _gpuEngineLuids.Clear();
        foreach (var (engine, sum) in _gpuEngines)
        {
            var luid = LuidOf(engine);
            var clamped = Math.Clamp(sum, 0, 100);
            if (!_gpuEngineLuids.TryGetValue(luid, out var best) || clamped > best) _gpuEngineLuids[luid] = clamped;
        }
        return new Dictionary<string, double>(_gpuEngineLuids, StringComparer.Ordinal);
    }

    /// "luid_0x" + 8 hex + "_0x" + 8 hex.
    private const int LuidTextLength = 7 + 8 + 3 + 8;

    /// The LUID of a "luid_0x00000000_0x0000D1F2…" engine as <see cref="GraphicsAdapter.Luid"/> writes it.
    private static string LuidOf(string engine) => string.Create(16, engine, static (dst, src) =>
    {
        for (var i = 0; i < 8; i++) dst[i] = char.ToLowerInvariant(src[7 + i]);
        for (var i = 0; i < 8; i++) dst[8 + i] = char.ToLowerInvariant(src[18 + i]);
    });

    /// Each physical disk's active time in percent (100 minus its idle time), by Windows' disk number; null when Windows has no disk counters.
    public IReadOnlyDictionary<int, double>? DiskActivity()
    {
        if (Counter(ref _disks, ref _disksTried, @"\PhysicalDisk(*)\% Idle Time") is not { } counter) return null;
        var activity = new Dictionary<int, double>();
        counter.ReadEach((instance, idle) =>
        {
            // "0 C:", "1 D: E:" (the disk number first); "_Total" is left out.
            var space = instance.IndexOf(' ');
            if (int.TryParse(space < 0 ? instance : instance[..space], out var number)) activity[number] = Math.Clamp(100 - idle, 0, 100);
        });
        return activity;
    }

    /// Every temperature Windows gives: the ACPI thermal zones (board), each graphics adapter and each drive that reports one.
    public List<TemperatureSensor> Temperatures(bool zones, bool gpus, bool drives)
    {
        var sensors = new List<TemperatureSensor>();
        if (zones) sensors.AddRange(Zones());
        if (gpus) sensors.AddRange(GpuTemperatures());
        if (drives) sensors.AddRange(DriveTemperatures());
        return sensors;
    }

    /// The ACPI thermal zones (\_TZ.xxx), in tenths of a kelvin where Windows has the precise counter, else in kelvin.
    private List<TemperatureSensor> Zones()
    {
        var sensors = new List<TemperatureSensor>();
        if (!_zonesTried)
        {
            _zonesTried = true;
            _zones = PdhCounter.Open(@"\Thermal Zone Information(*)\High Precision Temperature");
            _precise = _zones is not null;
            _zones ??= PdhCounter.Open(@"\Thermal Zone Information(*)\Temperature");
        }
        if (_zones is null) return sensors;
        _zones.ReadEach((instance, value) =>
        {
            var celsius = (_precise ? value / 10 : value) - Kelvin;
            if (celsius is <= 0 or >= HottestMax) return;
            var trimmed = instance.StartsWith(@"\_TZ.".AsSpan(), StringComparison.Ordinal) ? instance[5..] : instance;
            var name = trimmed.ToString();
            sensors.Add(new TemperatureSensor(ZoneIds + name, $"Thermal zone {name}", "ACPI", SensorGroup.Board, celsius));
        });
        return sensors;
    }

    private bool _precise;

    /// Each graphics adapter's own temperature (WDDM 2.5 drivers that report one; others are left out), named as Windows names the adapter.
    private static List<TemperatureSensor> GpuTemperatures() => GraphicsAdapters.Each<TemperatureSensor>((handle, luid) =>
    {
        var perf = stackalloc byte[PerfDataSize];
        new Span<byte>(perf, PerfDataSize).Clear(); // PhysicalAdapterIndex 0: the adapter itself
        if (!GraphicsAdapters.Query(handle, AdapterPerfData, perf, PerfDataSize)) return null;
        var celsius = *(uint*)(perf + TemperatureAt) / 10.0;
        if (celsius is <= 0 or >= HottestMax) return null;
        return new TemperatureSensor(GpuIds + luid, "GPU Temperature", GraphicsAdapters.NameOf(handle), SensorGroup.Gpu, celsius) { GpuLuid = luid };
    });
    /// Each physical disk's own temperature (the hottest of what it reports), named by its model; a drive Windows' driver gives none for is left out.
    private List<TemperatureSensor> DriveTemperatures()
    {
        var sensors = new List<TemperatureSensor>();
        var without = new List<int>();
        _diskList ??= PhysicalDisks.List();
        var buffer = new byte[TemperatureBufferSize];
        foreach (var disk in _diskList)
        {
            using var handle = CreateFile($@"\\.\PhysicalDrive{disk.Number}", 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (!handle.IsInvalid && DriveTemperature(handle, buffer) is { } celsius)
                sensors.Add(new TemperatureSensor(DiskIds + disk.Id, disk.Model, disk.Model, SensorGroup.Storage, celsius) { DiskNumber = disk.Number });
            else without.Add(disk.Number);
        }
        DisksWithoutTemperature = without;
        return sensors;
    }

    /// The disks the last drive read found no temperature for (their driver reports none: often SATA, USB).
    public IReadOnlyList<int> DisksWithoutTemperature { get; private set; } = Array.Empty<int>();

    private static double? DriveTemperature(SafeFileHandle disk, byte[] buffer)
    {
        var query = stackalloc int[3] { DeviceTemperatureProperty, 0, 0 }; // STORAGE_PROPERTY_QUERY: the property, PropertyStandardQuery, one spare byte
        Array.Clear(buffer);
        fixed (byte* output = buffer)
            if (!DeviceIoControl(disk, QueryProperty, query, 12, output, buffer.Length, out _, IntPtr.Zero)) return null;
        var count = Math.Min((int)BitConverter.ToUInt16(buffer, InfoCountAt), (buffer.Length - InfoAt) / InfoSize);
        double? hottest = null;
        for (var i = 0; i < count; i++)
        {
            double celsius = BitConverter.ToInt16(buffer, InfoAt + i * InfoSize + InfoTemperatureAt);
            if (celsius is > 0 and < HottestMax && (hottest is null || celsius > hottest)) hottest = celsius;
        }
        return hottest;
    }

    /// A disk was added or removed: the drives are listed again at the next read.
    public void RescanDisks() => _diskList = null;

    private static PdhCounter? Counter(ref PdhCounter? counter, ref bool tried, string path)
    {
        if (!tried)
        {
            tried = true;
            counter = PdhCounter.Open(path);
        }
        return counter;
    }

    public void Dispose()
    {
        _gpu?.Dispose();
        _disks?.Dispose();
        _zones?.Dispose();
        (_gpu, _disks, _zones, _gpuTried, _disksTried, _zonesTried, _diskList) = (null, null, null, false, false, false, null);
        DisksWithoutTemperature = Array.Empty<int>();
    }
}
