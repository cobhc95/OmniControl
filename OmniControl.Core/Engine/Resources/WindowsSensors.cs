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

    /// The graphics card's load in percent as Task Manager gives it (its busiest engine, every program added up), or null when Windows has
    /// no graphics engine counters (no WDDM driver).
    public double? GpuLoad()
    {
        if (Counter(ref _gpu, ref _gpuTried, @"\GPU Engine(*)\Utilization Percentage") is not { } counter) return null;
        // An instance is one program on one engine: "pid_1234_luid_0x..._0x..._phys_0_eng_3_engtype_3D"; an engine is the part from "luid".
        var engines = new Dictionary<string, double>();
        foreach (var (instance, value) in counter.Read())
        {
            var at = instance.IndexOf("luid", StringComparison.Ordinal);
            if (at < 0) continue;
            var engine = instance[at..];
            engines[engine] = engines.GetValueOrDefault(engine) + value;
        }
        return engines.Count == 0 ? 0 : Math.Clamp(engines.Values.Max(), 0, 100);
    }

    /// Each physical disk's active time in percent (100 minus its idle time), by Windows' disk number; null when Windows has no disk counters.
    public IReadOnlyDictionary<int, double>? DiskActivity()
    {
        if (Counter(ref _disks, ref _disksTried, @"\PhysicalDisk(*)\% Idle Time") is not { } counter) return null;
        var activity = new Dictionary<int, double>();
        foreach (var (instance, idle) in counter.Read())
        {
            // "0 C:", "1 D: E:" (the disk number first); "_Total" is left out.
            var space = instance.IndexOf(' ');
            if (int.TryParse(space < 0 ? instance : instance[..space], out var number)) activity[number] = Math.Clamp(100 - idle, 0, 100);
        }
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
        foreach (var (instance, value) in _zones.Read())
        {
            var celsius = (_precise ? value / 10 : value) - Kelvin;
            if (celsius is <= 0 or >= HottestMax) continue;
            var name = instance.StartsWith(@"\_TZ.", StringComparison.Ordinal) ? instance[5..] : instance;
            sensors.Add(new TemperatureSensor(ZoneIds + name, $"Thermal zone {name}", "ACPI", SensorGroup.Board, celsius));
        }
        return sensors;
    }

    private bool _precise;

    /// Each graphics adapter's own temperature (WDDM 2.5 drivers that report one; others are left out), named as Windows names the adapter.
    private static List<TemperatureSensor> GpuTemperatures()
    {
        var sensors = new List<TemperatureSensor>();
        var list = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(ref list) != 0 || list.Count == 0) return sensors;
        var adapters = new AdapterInfo[list.Count];
        fixed (AdapterInfo* first = adapters)
        {
            list.Adapters = first;
            if (D3DKMTEnumAdapters2(ref list) != 0) return sensors;
        }
        var perf = stackalloc byte[PerfDataSize];
        var registry = new char[RegistryInfoSize / 2];
        foreach (var adapter in adapters.AsSpan(0, (int)list.Count))
        {
            var handle = adapter.Adapter;
            try
            {
                new Span<byte>(perf, PerfDataSize).Clear(); // PhysicalAdapterIndex 0: the adapter itself
                if (!Query(handle, AdapterPerfData, perf, PerfDataSize)) continue;
                var celsius = *(uint*)(perf + TemperatureAt) / 10.0;
                if (celsius is <= 0 or >= HottestMax) continue;
                string name;
                fixed (char* text = registry)
                    name = Query(handle, AdapterRegistryInfo, text, RegistryInfoSize) ? new string(text).Trim() : string.Empty;
                if (name.Length == 0) name = "Graphics card";
                sensors.Add(new TemperatureSensor($"{GpuIds}{adapter.LuidHigh:x8}{adapter.LuidLow:x8}", "GPU Temperature", name, SensorGroup.Gpu, celsius));
            }
            finally
            {
                D3DKMTCloseAdapter(ref handle);
            }
        }
        return sensors;
    }

    private static bool Query(uint adapter, int type, void* data, int size)
    {
        var query = new QueryAdapterInfo { Adapter = adapter, Type = type, Data = data, DataSize = (uint)size };
        return D3DKMTQueryAdapterInfo(ref query) == 0;
    }

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
