using System.Net.NetworkInformation;

namespace OmniControl.Core.Engine.Resources;

/// One reading of the PC's resources (null where it could not be read): load in percent, memory in GB, network in bytes per second, temperatures in °C.
public sealed record ResourceSample(double? Cpu, double? Gpu, double? MemoryUsedGb, double? MemoryTotalGb, double? Download, double? Upload, string? Adapter,
    double? Disk, double? CpuTemp, double? GpuTemp, double? Hottest, string? HottestName)
{
    public static readonly ResourceSample None = new(null, null, null, null, null, null, null, null, null, null, null, null);

    /// The sensor each temperature reading came from (the chosen one, or Auto's pick); null when that reading was not wanted or found none.
    public TemperatureSensor? HottestSensor { get; init; }
    public TemperatureSensor? CpuTempSensor { get; init; }
    public TemperatureSensor? GpuTempSensor { get; init; }

    /// Every temperature sensor with its value now (only when <see cref="SampleParts.SensorList"/> was wanted).
    public IReadOnlyList<TemperatureSensor> TemperatureSensors { get; init; } = Array.Empty<TemperatureSensor>();

    public double? MemoryPercent => MemoryTotalGb > 0 ? MemoryUsedGb / MemoryTotalGb * 100 : null;

    /// Each physical disk's activity and temperature (only when <see cref="SampleParts.Disk"/> was wanted).
    public IReadOnlyList<DiskSample> Disks { get; init; } = Array.Empty<DiskSample>();

    /// Each graphics adapter's load and temperature (only when <see cref="SampleParts.EachGpu"/> was wanted).
    public IReadOnlyList<GpuSample> Gpus { get; init; } = Array.Empty<GpuSample>();

    /// Every adapter (not loopback or tunnel) with its speed now, for the network adapter menu.
    public IReadOnlyList<AdapterSpeed> Adapters { get; init; } = Array.Empty<AdapterSpeed>();

    /// The adapter the Download and Upload values were read from when each reading has its own choice in force (<see cref="SpeedAdapters"/>); null: the common choice's (<see cref="Adapter"/>).
    public string? DownloadFrom { get; init; }
    public string? UploadFrom { get; init; }

    /// True when <see cref="Adapter"/> is the busiest adapter (Auto, or a chosen adapter that is gone); false when the chosen adapter, or every adapter together, was read.
    public bool AdapterIsAuto { get; init; } = true;

    /// The parts this sample was read with (CPU and memory always are); a part left out is null.
    public SampleParts Parts { get; init; }
}

/// One physical disk: Windows' disk <paramref name="Number"/> (<see cref="PhysicalDisk.Number"/>), its active time in percent and its hottest
/// temperature in °C (null when the drive reports none, or that part was not wanted).
public sealed record DiskSample(int Number, double? Activity, double? Temperature)
{
    /// The sensor the temperature came from (the chosen one, or the drive's own); null when none was read.
    public TemperatureSensor? Sensor { get; init; }
}

/// One graphics adapter (<see cref="GraphicsAdapter.Id"/>): its load in percent and its temperature in °C (null when not read or not reported).
public sealed record GpuSample(string Id, double? Load, double? Temperature)
{
    /// The sensor the temperature came from (the chosen one, or the adapter's own); null when none was read.
    public TemperatureSensor? Sensor { get; init; }
}

/// Where a temperature sensor sits, in the order a menu lists them.
public enum SensorGroup { Cpu, Gpu, Storage, Board }

/// One temperature sensor (Windows' own, whose ids start with <c>win/</c>, or the sensor library's): a stable <paramref name="Id"/>, its
/// <paramref name="Name"/>, the (top-level) <paramref name="Hardware"/> it belongs to and its value in Celsius.
public sealed record TemperatureSensor(string Id, string Name, string Hardware, SensorGroup Group, double Value)
{
    /// The physical disk a drive's sensor belongs to (Windows' disk number); null for any other sensor.
    public int? DiskNumber { get; init; }

    /// The graphics adapter a Windows GPU sensor belongs to (<see cref="GraphicsAdapter.Luid"/>); null for any other sensor.
    public string? GpuLuid { get; init; }

    /// The name without the "CPU " or "GPU " that the group heading already says.
    public string Short => Group is SensorGroup.Cpu or SensorGroup.Gpu && Name.Length > 4 && Name[3] == ' ' && Name[..3] is "CPU" or "GPU" ? Name[4..] : Name;
}

/// The temperature sensor each temperature reading was set to (a <see cref="TemperatureSensor.Id"/>; each disk's by its number); null, absent
/// or gone = Auto. Equal when every choice is.
public sealed record SensorChoices(string? Hottest, string? Cpu, string? Gpu, IReadOnlyDictionary<int, string> Disks)
{
    public static readonly SensorChoices Auto = new(null, null, null, new Dictionary<int, string>());

    /// Each graphics adapter's chosen sensor, by <see cref="GraphicsAdapter.Id"/>.
    public IReadOnlyDictionary<string, string> Gpus { get; init; } = new Dictionary<string, string>();

    /// The graphics adapter the GPU usage and the GPU temperature (with no sensor chosen) are read from (<see cref="GraphicsAdapter.Id"/>);
    /// null or gone = Auto (the busiest, the ranking).
    public string? UsageGpu { get; init; }
    public string? TemperatureGpu { get; init; }

    /// Every chosen sensor.
    public IEnumerable<string> Ids => new[] { Hottest, Cpu, Gpu }.OfType<string>().Concat(Disks.Values).Concat(Gpus.Values);

    public bool Equals(SensorChoices? other) => other is not null && (Hottest, Cpu, Gpu, UsageGpu, TemperatureGpu) == (other.Hottest, other.Cpu, other.Gpu, other.UsageGpu, other.TemperatureGpu)
        && Same(Disks, other.Disks) && Same(Gpus, other.Gpus);

    private static bool Same<TKey>(IReadOnlyDictionary<TKey, string> a, IReadOnlyDictionary<TKey, string> b) =>
        a.Count == b.Count && a.All(d => b.TryGetValue(d.Key, out var id) && id == d.Value);

    public override int GetHashCode() => HashCode.Combine(Hottest, Cpu, Gpu, UsageGpu, TemperatureGpu, Disks.Count, Gpus.Count);
}

/// The network adapter the Download and Upload readings each follow on their own (an <see cref="AdapterSpeed.Id"/>); null, or an adapter that is gone
/// or not connected = the common choice of <see cref="ResourceSampler.Read"/>.
public sealed record SpeedAdapters(string? Download, string? Upload)
{
    public static readonly SpeedAdapters Auto = new(null, null);
}

/// The parts of a read a caller wants, beyond CPU load and memory (two cheap calls, always read): the network speeds, the adapter list (only
/// for the adapter menu), and each sensor reading (disk activity and the disks' temperatures apart), whose sources open only for these.
[Flags]
public enum SampleParts
{
    None = 0, Network = 1, Adapters = 2, Gpu = 4, Disk = 8, CpuTemp = 16, GpuTemp = 32, Hottest = 64, SensorList = 128, DiskTemp = 256,
    /// Each graphics adapter apart (with <see cref="Gpu"/> its load, with <see cref="GpuTemp"/> its temperature).
    EachGpu = 512,
    Sensors = Gpu | Disk | CpuTemp | GpuTemp | Hottest | DiskTemp | EachGpu,
    All = Network | Sensors,
}

/// Reads the PC's resources, one <see cref="Read"/> per tick on one worker thread (not thread-safe): CPU load from GetSystemTimes, memory from
/// GlobalMemoryStatusEx, network totals from Windows' adapter counters, and GPU load, disk activity and temperatures through
/// <see cref="SensorReader"/> (Windows' own sources, the sensor library only for what Windows does not give). Rates are the change since the previous read.
public sealed class ResourceSampler : IDisposable
{
    private const double Gigabyte = 1024d * 1024 * 1024;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, (long Received, long Sent)> _adapters = new();
    private readonly SensorReader _sensors = new();
    private (ulong Idle, ulong Busy)? _cpu;
    private double _lastNetAt;

    /// Why the sensor library could not start (null: it runs, is not needed, or has not been asked yet).
    public string? SensorProblem => _sensors.Problem;

    /// <param name="adapter">Which adapter the network speeds are read from: null = the busiest one (Auto), <see cref="AdapterSpeed.All"/> = all
    /// added together, else an adapter's id; a chosen adapter that is gone or not connected reads as Auto.</param>
    /// <param name="sensors">The temperature sensor each temperature reading follows (null: all on Auto).</param>
    /// <param name="speeds">The adapter Download and Upload each follow on their own (null: both follow <paramref name="adapter"/>).</param>
    public ResourceSample Read(SampleParts wanted, string? adapter, SensorChoices? sensors = null, SpeedAdapters? speeds = null)
    {
        var net = (wanted & SampleParts.Network) != 0 ? Network(adapter, speeds ?? SpeedAdapters.Auto, (wanted & SampleParts.Adapters) != 0) : NoNetwork();
        var (used, total) = Memory();
        var sample = new ResourceSample(Cpu(), null, used, total, net.OwnDown?.Speed ?? net.Download, net.OwnUp?.Speed ?? net.Upload, net.Adapter, null, null, null, null, null)
        {
            Adapters = net.Adapters,
            AdapterIsAuto = net.Auto,
            DownloadFrom = net.OwnDown?.Name,
            UploadFrom = net.OwnUp?.Name,
            Parts = wanted,
        };
        return (wanted & SampleParts.Sensors) != 0 ? _sensors.Add(sample, wanted, sensors ?? SensorChoices.Auto) : sample;
    }

    /// The network is not wanted: its counters are forgotten, so the first read after it is wanted again starts afresh (never a rate over the gap).
    private NetRead NoNetwork()
    {
        _adapters.Clear();
        _lastNetAt = 0;
        return new(null, null, null, true, Array.Empty<AdapterSpeed>());
    }

    /// One look at the network: the common choice's speeds and adapter, whether that is Auto, the adapter list (when asked) and the own choices' speeds in bytes per second.
    private sealed record NetRead(double? Download, double? Upload, string? Adapter, bool Auto, IReadOnlyList<AdapterSpeed> Adapters)
    {
        public (string Name, double Speed)? OwnDown { get; init; }
        public (string Name, double Speed)? OwnUp { get; init; }
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
    private NetRead Network(string? wanted, SpeedAdapters own, bool list)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var seconds = now - _lastNetAt;
        var first = _lastNetAt == 0;
        _lastNetAt = now;
        var scale = first || seconds <= 0 ? 0 : 1 / seconds;
        var adapters = list ? new List<AdapterSpeed>() : null;
        long down = 0, up = 0, busiest = -1;
        (string Name, double Down, double Up)? busiestAdapter = null, chosen = null;
        (string Name, double Speed)? ownDown = null, ownUp = null;
        var rows = AdapterTable.Read(); // the one look per read: flags, state and counters of every interface
        foreach (var nic in Adapters(rows))
        {
            var row = rows.GetValueOrDefault(nic.Id);
            if (row.Filter) continue;
            var hardware = !rows.ContainsKey(nic.Id) || row.Hardware;
            if (!row.Up)
            {
                adapters?.Add(new AdapterSpeed(nic.Id, nic.Name, AdapterSpeed.KindOf(nic.Type, nic.Description, hardware), false, 0, 0));
                continue;
            }
            var total = (BytesReceived: row.Received, BytesSent: row.Sent);
            long d = 0, u = 0;
            var known = _adapters.TryGetValue(nic.Id, out var before) && total.BytesReceived >= before.Received && total.BytesSent >= before.Sent;
            if (known) (d, u) = (total.BytesReceived - before.Received, total.BytesSent - before.Sent);
            _adapters[nic.Id] = (total.BytesReceived, total.BytesSent);
            adapters?.Add(new AdapterSpeed(nic.Id, nic.Name, AdapterSpeed.KindOf(nic.Type, nic.Description, hardware), true, d * scale, u * scale));
            if (!known) continue;
            (down, up) = (down + d, up + u);
            if (d + u > busiest) (busiest, busiestAdapter) = (d + u, (nic.Name, d * scale, u * scale));
            if (nic.Id == wanted) chosen = (nic.Name, d * scale, u * scale);
            if (nic.Id == own.Download) ownDown = (nic.Name, d * scale);
            if (nic.Id == own.Upload) ownUp = (nic.Name, u * scale);
        }
        IReadOnlyList<AdapterSpeed> shown = adapters ?? (IReadOnlyList<AdapterSpeed>)Array.Empty<AdapterSpeed>();
        if (first || seconds <= 0) return new(null, null, null, true, shown);
        if (wanted == AdapterSpeed.All) return new(down * scale, up * scale, null, false, shown) { OwnDown = ownDown, OwnUp = ownUp };
        if (chosen is { } c) return new(c.Down, c.Up, c.Name, false, shown) { OwnDown = ownDown, OwnUp = ownUp };
        return new(busiestAdapter?.Down ?? 0, busiestAdapter?.Up ?? 0, busiestAdapter?.Name, true, shown) { OwnDown = ownDown, OwnUp = ownUp };
    }

    private sealed record Nic(string Id, string Name, NetworkInterfaceType Type, string Description);

    private IReadOnlyList<Nic>? _nics;
    private int _upCount = -1;

    /// The adapters Windows lists (not loopback or tunnel), listed again only when the interface table shows a change (an interface came up
    /// or went down, or a listed one is gone), so a read costs one table look, not a full adapter listing.
    private IReadOnlyList<Nic> Adapters(Dictionary<string, AdapterRow> rows)
    {
        var upCount = rows.Values.Count(r => r.Up && !r.Filter);
        if (_nics is { } known && upCount == _upCount && known.All(n => rows.ContainsKey(n.Id))) return known;
        _upCount = upCount;
        return _nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => new Nic(n.Id, n.Name, n.NetworkInterfaceType, n.Description)).ToList();
    }

    /// A disk or graphics adapter was added or removed: the next read that has them open lists them again (any thread).
    public void Rescan() => _sensors.Rescan();

    /// Gives back the sensor sources' memory and handles (the sensor library's too); the next <see cref="Read"/> that needs one opens it again
    /// (a library that failed to start is not retried).
    public void CloseSensors() => _sensors.Close();
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
