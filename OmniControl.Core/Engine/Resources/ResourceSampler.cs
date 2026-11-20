using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;

namespace OmniControl.Core.Engine.Resources;

/// One reading of the PC's resources (null where it could not be read): load in percent, memory in GB, network in bytes per second, temperatures in Â°C.
public sealed record ResourceSample(double? Cpu, double? Gpu, double? MemoryUsedGb, double? MemoryTotalGb, double? Download, double? Upload, string? Adapter,
    double? Disk, double? CpuTemp, double? GpuTemp, double? Hottest, string? HottestName)
{
    public static readonly ResourceSample None = new(null, null, null, null, null, null, null, null, null, null, null, null);

    /// The name of the temperature sensor the Hottest reading follows when one was chosen and still exists; null: the hottest of all.
    public string? ChosenSensor { get; init; }

    /// Every temperature sensor with its value now (only when <see cref="SampleParts.SensorList"/> was wanted).
    public IReadOnlyList<TemperatureSensor> TemperatureSensors { get; init; } = Array.Empty<TemperatureSensor>();

    public double? MemoryPercent => MemoryTotalGb > 0 ? MemoryUsedGb / MemoryTotalGb * 100 : null;

    /// Each physical disk's activity and temperature (only when <see cref="SampleParts.Disk"/> was wanted).
    public IReadOnlyList<DiskSample> Disks { get; init; } = Array.Empty<DiskSample>();

    /// Every adapter (not loopback or tunnel) with its speed now, for the network adapter menu.
    public IReadOnlyList<AdapterSpeed> Adapters { get; init; } = Array.Empty<AdapterSpeed>();

    /// True when <see cref="Adapter"/> is the busiest adapter (Auto, or a chosen adapter that is gone); false when the chosen adapter, or every adapter together, was read.
    public bool AdapterIsAuto { get; init; } = true;

    /// The parts this sample was read with (CPU and memory always are); a part left out is null.
    public SampleParts Parts { get; init; }
}

/// One physical disk as the sensor library read it: Windows' disk <paramref name="Number"/> (<see cref="PhysicalDisk.Number"/>), its active
/// time in percent and its hottest temperature in °C (null when the drive reports none).
public sealed record DiskSample(int Number, double? Activity, double? Temperature);

/// Where a temperature sensor sits, in the order a menu lists them.
public enum SensorGroup { Cpu, Gpu, Storage, Board }

/// One temperature sensor of the library: a stable <paramref name="Id"/>, its <paramref name="Name"/>, the (top-level) <paramref name="Hardware"/> it belongs to and its value in Celsius.
public sealed record TemperatureSensor(string Id, string Name, string Hardware, SensorGroup Group, double Value)
{
    /// The name without the "CPU " or "GPU " that the group heading already says.
    public string Short => Group is SensorGroup.Cpu or SensorGroup.Gpu && Name.Length > 4 && Name[3] == ' ' && Name[..3] is "CPU" or "GPU" ? Name[4..] : Name;
}

/// The parts of a read a caller wants, beyond CPU load and memory (two cheap calls, always read): the network speeds, the adapter list (only
/// for the adapter menu), and each reading of the sensor library, which is opened, and given only the hardware groups it needs, only for these.
[Flags]
public enum SampleParts
{
    None = 0, Network = 1, Adapters = 2, Gpu = 4, Disk = 8, CpuTemp = 16, GpuTemp = 32, Hottest = 64, SensorList = 128,
    Sensors = Gpu | Disk | CpuTemp | GpuTemp | Hottest,
    All = Network | Sensors,
}

/// Reads the PC's resources, one <see cref="Read"/> per tick on one worker thread (not thread-safe): CPU load from GetSystemTimes, memory from
/// GlobalMemoryStatusEx, network totals from Windows' adapter counters, and GPU load, disk activity and temperatures from LibreHardwareMonitor
/// (PawnIO build: no WinRing0; without the PawnIO driver the CPU's own temperature is not readable and stays null). Rates are the change since the previous read.
public sealed class ResourceSampler : IDisposable
{
    private const double Gigabyte = 1024d * 1024 * 1024, HottestMax = 150; // a sensor above this is a misread, not a temperature
    private static readonly string[] NotTemperatures = { "Distance", "Limit", "Critical", "Warning", "TjMax" };

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, (long Received, long Sent)> _adapters = new();
    private (ulong Idle, ulong Busy)? _cpu;
    private double _lastNetAt;
    private Computer? _sensors;
    private bool _sensorsTried;
    private volatile bool _rescanStorage;
    private TimeSpan? _sensedAt, _storageAt; // when the library and the drives were last updated (null: update at the next read)
    private ResourceSample _sensed = ResourceSample.None; // the sensor values of that update
    private List<ISensor> _temps = new(); // the temperature sensors of that update

    /// Why the sensor library could not start (null: it runs, or has not been asked yet).
    public string? SensorProblem { get; private set; }

    /// <param name="adapter">Which adapter the network speeds are read from: null = the busiest one (Auto), <see cref="AdapterSpeed.All"/> = all
    /// added together, else an adapter's id; a chosen adapter that is gone or not connected reads as Auto.</param>
    /// <param name="sensor">The temperature sensor the Hottest reading follows (its <see cref="TemperatureSensor.Id"/>); null or gone = the hottest of all.</param>
    public ResourceSample Read(SampleParts wanted, string? adapter, string? sensor = null)
    {
        var net = (wanted & SampleParts.Network) != 0 ? Network(adapter, (wanted & SampleParts.Adapters) != 0) : NoNetwork();
        var (used, total) = Memory();
        var sample = new ResourceSample(Cpu(), null, used, total, net.Download, net.Upload, net.Adapter, null, null, null, null, null)
        {
            Adapters = net.Adapters,
            AdapterIsAuto = net.Auto,
            Parts = wanted,
        };
        return (wanted & SampleParts.Sensors) != 0 && Sensors(wanted) is { } computer ? WithSensors(sample, computer, wanted, sensor) : sample;
    }

    /// The network is not wanted: its counters are forgotten, so the first read after it is wanted again starts afresh (never a rate over the gap).
    private (double? Download, double? Upload, string? Adapter, bool Auto, IReadOnlyList<AdapterSpeed> Adapters) NoNetwork()
    {
        _adapters.Clear();
        _lastNetAt = 0;
        return (null, null, null, true, Array.Empty<AdapterSpeed>());
    }

    private double? Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var now = (Idle: idle, Busy: kernel + user); // kernel time includes idle time
        var before = _cpu;
        _cpu = now;
        if (before is not { } b || now.Busy <= b.Busy) return null;
        return Math.Clamp(100 * (1 - (double)(now.Idle - b.Idle) / (now.Busy - b.Busy)), 0, 100);
    }

    private static (double?, double?) Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return (null, null);
        return ((status.TotalPhys - status.AvailPhys) / Gigabyte, status.TotalPhys / Gigabyte);
    }

    /// Bytes per second down and up of the <paramref name="wanted"/> adapter (the busiest one, one adapter, or all together), the name of the
    /// adapter read, whether that is the busiest one (Auto), and, when <paramref name="list"/>, every adapter's speed for the menu.
    private (double? Download, double? Upload, string? Adapter, bool Auto, IReadOnlyList<AdapterSpeed> Adapters) Network(string? wanted, bool list)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var seconds = now - _lastNetAt;
        var first = _lastNetAt == 0;
        _lastNetAt = now;
        var scale = first || seconds <= 0 ? 0 : 1 / seconds;
        var adapters = list ? new List<AdapterSpeed>() : null;
        long down = 0, up = 0, busiest = -1;
        (string Name, double Down, double Up)? busiestAdapter = null, chosen = null;
        var flags = AdapterFlags.Read();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var hardware = true;
            if (flags.TryGetValue(nic.Id, out var flag))
            {
                if (flag.Filter) continue;
                hardware = flag.Hardware;
            }
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                adapters?.Add(new AdapterSpeed(nic.Id, nic.Name, AdapterSpeed.KindOf(nic, hardware), false, 0, 0));
                continue;
            }
            var stats = nic.GetIPStatistics();
            var total = (stats.BytesReceived, stats.BytesSent);
            long d = 0, u = 0;
            var known = _adapters.TryGetValue(nic.Id, out var before) && total.BytesReceived >= before.Received && total.BytesSent >= before.Sent;
            if (known) (d, u) = (total.BytesReceived - before.Received, total.BytesSent - before.Sent);
            _adapters[nic.Id] = (total.BytesReceived, total.BytesSent);
            adapters?.Add(new AdapterSpeed(nic.Id, nic.Name, AdapterSpeed.KindOf(nic, hardware), true, d * scale, u * scale));
            if (!known) continue;
            (down, up) = (down + d, up + u);
            if (d + u > busiest) (busiest, busiestAdapter) = (d + u, (nic.Name, d * scale, u * scale));
            if (nic.Id == wanted) chosen = (nic.Name, d * scale, u * scale);
        }
        IReadOnlyList<AdapterSpeed> shown = adapters ?? (IReadOnlyList<AdapterSpeed>)Array.Empty<AdapterSpeed>();
        if (first || seconds <= 0) return (null, null, null, true, shown);
        if (wanted == AdapterSpeed.All) return (down * scale, up * scale, null, false, shown);
        if (chosen is { } c) return (c.Down, c.Up, c.Name, false, shown);
        return (busiestAdapter?.Down ?? 0, busiestAdapter?.Up ?? 0, busiestAdapter?.Name, true, shown);
    }

    /// The sensor library with exactly the hardware groups <paramref name="wanted"/> needs (the library adds and drops a group when its switch
    /// changes while open), opened on the first read that needs it (it takes a moment); null when it cannot start.
    private Computer? Sensors(SampleParts wanted)
    {
        bool Any(SampleParts parts) => (wanted & parts) != 0;
        var (cpu, gpu, storage, board) = (Any(SampleParts.CpuTemp | SampleParts.Hottest), Any(SampleParts.Gpu | SampleParts.GpuTemp | SampleParts.Hottest),
            Any(SampleParts.Disk | SampleParts.Hottest), Any(SampleParts.Hottest));
        if (_sensors is { } open)
        {
            // The library lists the drives only when its storage group is made: a disk added or removed needs the group made again.
            if (_rescanStorage && open.IsStorageEnabled)
            {
                open.IsStorageEnabled = false;
                open.IsStorageEnabled = true;
                (_sensedAt, _storageAt) = (null, null);
            }
            _rescanStorage = false;
            if ((open.IsCpuEnabled, open.IsGpuEnabled, open.IsStorageEnabled, open.IsMotherboardEnabled) != (cpu, gpu, storage, board))
            {
                (open.IsCpuEnabled, open.IsGpuEnabled, open.IsStorageEnabled, open.IsMotherboardEnabled) = (cpu, gpu, storage, board);
                (_sensedAt, _storageAt) = (null, null); // a group just added is read at once
            }
            return open;
        }
        if (_sensorsTried) return null;
        _sensorsTried = true;
        try
        {
            var computer = new Computer { IsCpuEnabled = cpu, IsGpuEnabled = gpu, IsStorageEnabled = storage, IsMotherboardEnabled = board };
            computer.Open();
            _sensors = computer;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // a third-party library over drivers and vendor DLLs: any failure means "no sensors", never a crash
        {
            SensorProblem = ex.Message;
        }
        return _sensors;
    }

    /// The sample with the wanted sensor readings: the library is updated at most every <see cref="CoreTiming.SensorEvery"/> (the drives every
    /// <see cref="CoreTiming.StorageSensorEvery"/>), so a fast read (the gadget's 100 ms) costs only CPU, memory and network.
    private ResourceSample WithSensors(ResourceSample sample, Computer computer, SampleParts wanted, string? sensor)
    {
        var now = _clock.Elapsed;
        if (_sensedAt is not { } sensed || now - sensed >= CoreTiming.SensorEvery)
        {
            var readStorage = _storageAt is not { } stored || now - stored >= CoreTiming.StorageSensorEvery;
            if (readStorage) _storageAt = now;
            _sensedAt = now;
            _sensed = Sense(computer, readStorage);
        }
        double? If(SampleParts part, double? value) => (wanted & part) != 0 ? value : null;
        var chosen = sensor is null ? null : _temps.FirstOrDefault(s => s.Identifier.ToString() == sensor);
        var hot = chosen ?? _temps.MaxBy(s => s.Value);
        var hottest = (wanted & SampleParts.Hottest) != 0 && hot is not null ? Describe(hot) : null;
        return sample with
        {
            Gpu = If(SampleParts.Gpu, _sensed.Gpu),
            Disk = If(SampleParts.Disk, _sensed.Disk),
            Disks = (wanted & SampleParts.Disk) != 0 ? _sensed.Disks : Array.Empty<DiskSample>(),
            CpuTemp = If(SampleParts.CpuTemp, _sensed.CpuTemp),
            GpuTemp = If(SampleParts.GpuTemp, _sensed.GpuTemp),
            Hottest = hottest?.Value,
            HottestName = hottest?.Hardware,
            ChosenSensor = chosen is not null ? hottest?.Name : null,
            TemperatureSensors = (wanted & SampleParts.SensorList) != 0 ? _temps.Select(Describe).OrderBy(t => t.Group).ToList() : Array.Empty<TemperatureSensor>(),
        };
    }

    /// A sensor as a menu lists it: its group and (top-level) hardware; a drive is named by its model.
    private static TemperatureSensor Describe(ISensor s)
    {
        var top = s.Hardware;
        while (top.Parent is { } parent) top = parent;
        var group = top.HardwareType switch
        {
            HardwareType.Cpu => SensorGroup.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorGroup.Gpu,
            HardwareType.Storage => SensorGroup.Storage,
            _ => SensorGroup.Board,
        };
        var name = group == SensorGroup.Storage ? (s.Name == "Temperature" ? top.Name : $"{top.Name} · {s.Name}") : s.Name;
        return new TemperatureSensor(s.Identifier.ToString(), name, top.Name, group, s.Value ?? 0);
    }

    private ResourceSample Sense(Computer computer, bool readStorage)
    {
        var all = new List<ISensor>();
        foreach (var hardware in computer.Hardware.SelectMany(h => h.SubHardware.Prepend(h)))
        {
            if (readStorage || hardware.HardwareType != HardwareType.Storage) hardware.Update();
            all.AddRange(hardware.Sensors);
        }
        static bool Gpu(ISensor s) => s.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var temps = all.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < (float)HottestMax && !NotTemperatures.Any(s.Name.Contains)).ToList();
        _temps = temps;
        static bool Activity(ISensor s) => s.SensorType == SensorType.Load && s.Name == "Total Activity";
        return ResourceSample.None with
        {
            Disks = computer.Hardware.OfType<StorageDevice>().Where(d => d.Storage is not null)
                .Select(d => new DiskSample(d.Storage.DriveNumber, Max(d.Sensors.Where(Activity)), Max(temps.Where(t => t.Hardware == d)))).ToList(),
            Gpu = Max(all.Where(s => Gpu(s) && s.SensorType == SensorType.Load && s.Name is "GPU Core" or "D3D 3D")),
            Disk = Max(all.Where(s => s.Hardware.HardwareType == HardwareType.Storage && Activity(s))),
            CpuTemp = Max(temps.Where(s => s.Hardware.HardwareType == HardwareType.Cpu)),
            GpuTemp = Max(temps.Where(Gpu)),
        };
    }

    private static double? Max(IEnumerable<ISensor> sensors) => sensors.Select(s => s.Value).Where(v => v is not null).Max();

    /// A disk was added or removed: the next read that has the drives open lists them again (any thread).
    public void RescanStorage() => _rescanStorage = true;

    /// Closes the sensor library to give back its memory and driver handles; the next <see cref="Read"/> that needs it opens it again (a
    /// library that failed to start is not retried).
    public void CloseSensors()
    {
        if (_sensors is null) return;
        _sensors.Close();
        _sensors = null;
        _temps = new();
        _sensorsTried = false;
        (_sensedAt, _storageAt) = (null, null);
    }

    public void Dispose() => CloseSensors();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
