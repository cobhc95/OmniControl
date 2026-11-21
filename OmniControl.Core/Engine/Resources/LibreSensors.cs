using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;

namespace OmniControl.Core.Engine.Resources;

/// The sensor groups of LibreHardwareMonitor a read asks for.
internal readonly record struct LibreGroups(bool Cpu, bool Gpu, bool Storage, bool Board)
{
    public bool Any => Cpu || Gpu || Storage || Board;

    public static LibreGroups Of(SensorGroup group) => new(group == SensorGroup.Cpu, group == SensorGroup.Gpu, group == SensorGroup.Storage, group == SensorGroup.Board);

    public static LibreGroups operator |(LibreGroups a, LibreGroups b) => new(a.Cpu || b.Cpu, a.Gpu || b.Gpu, a.Storage || b.Storage, a.Board || b.Board);
}

/// What one update of the library gave: the graphics card's load, the drives' activity and temperature, the CPU's and graphics card's
/// hottest temperature and every temperature sensor.
internal sealed record LibreReading(double? Gpu, double? Disk, IReadOnlyList<DiskSample> Disks, double? CpuTemp, double? GpuTemp, IReadOnlyList<TemperatureSensor> Temperatures)
{
    public static readonly LibreReading None = new(null, null, Array.Empty<DiskSample>(), null, null, Array.Empty<TemperatureSensor>());
}

/// LibreHardwareMonitor (PawnIO build: no WinRing0; without the PawnIO driver the CPU's own temperature is not readable), kept only for what
/// Windows does not give itself (<see cref="WindowsSensors"/>): the CPU package and the board's chips, and a graphics card or drive whose
/// driver reports no temperature. Opened with only the hardware groups asked for (the library adds and drops a group when its switch
/// changes while open) and closed when none is asked for; its assembly is loaded only when this class is first made. One worker thread only.
internal sealed class LibreSensors : IDisposable
{
    private static readonly string[] NotTemperatures = { "Distance", "Limit", "Critical", "Warning", "TjMax" };
    private const double HottestMax = 150; // a sensor above this is a misread, not a temperature

    private Computer? _computer;
    private bool _tried;
    private volatile bool _rescanStorage;

    /// Why the library could not start (null: it runs, or has not been asked yet).
    public string? Problem { get; private set; }

    /// Opens the library, or changes its groups, to exactly <paramref name="groups"/>; true when a group was added or the drives listed
    /// again (read everything at once), false otherwise. Null when the library cannot start (it is not tried again).
    public bool? Use(LibreGroups groups)
    {
        if (_computer is { } open)
        {
            var fresh = false;
            // The library lists the drives only when its storage group is made: a disk added or removed needs the group made again.
            if (_rescanStorage && open.IsStorageEnabled)
            {
                open.IsStorageEnabled = false;
                open.IsStorageEnabled = true;
                fresh = true;
            }
            _rescanStorage = false;
            if ((open.IsCpuEnabled, open.IsGpuEnabled, open.IsStorageEnabled, open.IsMotherboardEnabled) != (groups.Cpu, groups.Gpu, groups.Storage, groups.Board))
            {
                (open.IsCpuEnabled, open.IsGpuEnabled, open.IsStorageEnabled, open.IsMotherboardEnabled) = (groups.Cpu, groups.Gpu, groups.Storage, groups.Board);
                fresh = true;
            }
            return fresh;
        }
        if (_tried) return null;
        _tried = true;
        try
        {
            var computer = new Computer { IsCpuEnabled = groups.Cpu, IsGpuEnabled = groups.Gpu, IsStorageEnabled = groups.Storage, IsMotherboardEnabled = groups.Board };
            computer.Open();
            _computer = computer;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) // a third-party library over drivers and vendor DLLs: any failure means "no sensors", never a crash
        {
            Problem = ex.Message;
            return null;
        }
    }

    /// Updates the hardware that is due and gives its readings.
    /// <param name="readTemps">Update the hardware that gives only temperatures here (the CPU, the board), and a graphics card unless <paramref name="gpuLoad"/>.</param>
    public LibreReading Read(bool readStorage, bool readTemps, bool gpuLoad)
    {
        if (_computer is not { } computer) return LibreReading.None;
        var all = new List<ISensor>();
        foreach (var hardware in computer.Hardware.SelectMany(h => h.SubHardware.Prepend(h)))
        {
            var update = hardware.HardwareType switch
            {
                HardwareType.Storage => readStorage,
                HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => gpuLoad || readTemps,
                _ => readTemps,
            };
            if (update) hardware.Update();
            all.AddRange(hardware.Sensors);
        }
        static bool Gpu(ISensor s) => s.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var temps = all.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < (float)HottestMax && !NotTemperatures.Any(s.Name.Contains)).ToList();
        static bool Activity(ISensor s) => s.SensorType == SensorType.Load && s.Name == "Total Activity";
        return new LibreReading(
            Max(all.Where(s => Gpu(s) && s.SensorType == SensorType.Load && s.Name is "GPU Core" or "D3D 3D")),
            Max(all.Where(s => s.Hardware.HardwareType == HardwareType.Storage && Activity(s))),
            computer.Hardware.OfType<StorageDevice>().Where(d => d.Storage is not null)
                .Select(d => new DiskSample(d.Storage.DriveNumber, Max(d.Sensors.Where(Activity)), Max(temps.Where(t => t.Hardware == d)))).ToList(),
            Max(temps.Where(s => s.Hardware.HardwareType == HardwareType.Cpu)),
            Max(temps.Where(Gpu)),
            temps.Select(Describe).ToList());
    }

    /// Whether the open library has a sensor with this id (a chosen sensor out of range now, not gone).
    public bool Has(string id) => _computer?.Hardware.SelectMany(h => h.SubHardware.Prepend(h)).SelectMany(h => h.Sensors).Any(s => s.Identifier.ToString() == id) == true;

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
        return new TemperatureSensor(s.Identifier.ToString(), name, top.Name, group, s.Value ?? 0)
        {
            DiskNumber = top is StorageDevice { Storage: not null } drive ? drive.Storage.DriveNumber : null,
        };
    }

    private static double? Max(IEnumerable<ISensor> sensors) => sensors.Select(s => s.Value).Where(v => v is not null).Max();

    /// A disk was added or removed: the next read that has the drives open lists them again (any thread).
    public void RescanStorage() => _rescanStorage = true;

    /// Closes the library to give back its memory and driver handles; the next <see cref="Use"/> opens it again (a library that failed to
    /// start is not retried).
    public void Close()
    {
        if (_computer is null) return;
        _computer.Close();
        _computer = null;
        _tried = false;
    }

    public void Dispose() => Close();
}
