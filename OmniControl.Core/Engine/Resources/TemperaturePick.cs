namespace OmniControl.Core.Engine.Resources;

/// Which sensor is "the CPU temperature" and "the GPU temperature" on Auto, by the sensor library's names: the sensors of the best rank
/// present give the reading, the hottest of them (several CPUs or graphics cards).
internal static class TemperaturePick
{
    /// The CPU: the package (Intel "CPU Package", AMD "Core (Tctl/Tdie)" / "Core (Tdie)"), then Tctl alone (offset for fans), then the cores
    /// and CCDs, then any other CPU sensor; a sensor outside the CPU group (an ACPI thermal zone) only when the CPU has none.
    public static TemperatureSensor? Cpu(IEnumerable<TemperatureSensor> sensors) => Pick(sensors, s => s.Group == SensorGroup.Cpu ? CpuRank(s.Name) : 4);

    /// The graphics card: the core or edge ("GPU Core", Windows' "GPU Temperature"), then hot spot, then unknown ones, the memory, junction
    /// and voltage-regulator ones last.
    public static TemperatureSensor? Gpu(IEnumerable<TemperatureSensor> sensors) => Pick(sensors, s => GpuRank(s.Name));

    internal static int CpuRank(string name)
    {
        if (Has(name, "CCD")) return 2; // "CCD1 (Tdie)" says Tdie too: a CCD only without a package
        if (Has(name, "Package") || Has(name, "Tdie")) return 0;
        if (Has(name, "Tctl")) return 1;
        if (Has(name, "Core")) return 2;
        return 3;
    }

    internal static int GpuRank(string name)
    {
        if (Has(name, "Memory") || Has(name, "Junction") || Has(name, "VR") || Has(name, "SoC")) return 3;
        if (Has(name, "Hot Spot") || Has(name, "Hotspot")) return 1;
        if (Has(name, "Core") || Has(name, "Edge") || name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase) || name.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) return 0;
        return 2;
    }

    private static TemperatureSensor? Pick(IEnumerable<TemperatureSensor> sensors, Func<TemperatureSensor, int> rank) =>
        sensors.GroupBy(rank).MinBy(g => g.Key)?.MaxBy(s => s.Value);

    private static bool Has(string name, string part) => name.Contains(part, StringComparison.OrdinalIgnoreCase);
}
