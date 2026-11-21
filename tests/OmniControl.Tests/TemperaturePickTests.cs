using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// Which sensor is "the GPU temperature" and "the CPU temperature" on Auto, by the sensor library's names.
public class TemperaturePickTests
{
    private static double? Gpu(params (string Name, double Value)[] sensors) =>
        TemperaturePick.Gpu(sensors.Select((s, i) => new TemperatureSensor($"gpu/{i}", s.Name, "Graphics card", SensorGroup.Gpu, s.Value)))?.Value;

    private static double? Cpu(params (string Name, double Value, SensorGroup Group)[] sensors) =>
        TemperaturePick.Cpu(sensors.Select((s, i) => new TemperatureSensor($"cpu/{i}", s.Name, "Processor", s.Group, s.Value)))?.Value;

    [Fact]
    public void GpuCore_BeatsHotterMemoryJunctionAndHotSpot()
        => Assert.Equal(65, Gpu(("GPU Memory Junction", 92), ("GPU Hot Spot", 80), ("GPU Core", 65), ("GPU VR SoC", 70)));

    [Fact]
    public void GpuEdge_AndWindowsGpuTemperature_CountAsCore()
    {
        Assert.Equal(60, Gpu(("GPU Memory", 90), ("GPU Edge", 60), ("GPU Hot Spot", 75)));
        Assert.Equal(55, Gpu(("GPU Temperature", 55), ("GPU Hot Spot", 75)));
    }

    [Fact]
    public void GpuHotSpot_IsNextWhenThereIsNoCore()
        => Assert.Equal(75, Gpu(("GPU Memory Junction", 90), ("GPU Hot Spot", 75), ("GPU Hot Spot #1", 74)));

    [Fact]
    public void GpuUnknownSensor_ComesBeforeMemory()
        => Assert.Equal(40, Gpu(("GPU Board", 40), ("GPU Memory #1", 85)));

    [Fact]
    public void GpuMemoryJunctionAndRegulator_AreTheLastResort()
        => Assert.Equal(90, Gpu(("GPU Memory Junction", 90), ("GPU VR SoC", 70), ("GPU VR VDDC", 60)));

    [Fact]
    public void SeveralGpus_TakeTheHottestCore()
        => Assert.Equal(70, Gpu(("GPU Core", 50), ("GPU Core", 70), ("GPU Hot Spot", 95)));

    [Fact]
    public void NoSensors_GiveNothing()
    {
        Assert.Null(Gpu());
        Assert.Null(Cpu());
    }

    [Fact]
    public void IntelPackage_BeatsHotterCores()
        => Assert.Equal(60, Cpu(("CPU Package", 60, SensorGroup.Cpu), ("P-Core #1", 72, SensorGroup.Cpu), ("Core Max", 72, SensorGroup.Cpu), ("CPU Core #2", 70, SensorGroup.Cpu)));

    [Fact]
    public void AmdTctlTdie_BeatsCcds()
        => Assert.Equal(55, Cpu(("Core (Tctl/Tdie)", 55, SensorGroup.Cpu), ("CCD1 (Tdie)", 61, SensorGroup.Cpu), ("CCDs Max (Tdie)", 61, SensorGroup.Cpu)));

    [Fact]
    public void AmdTdie_BeatsTctlWithItsOffset()
        => Assert.Equal(50, Cpu(("Core (Tctl)", 70, SensorGroup.Cpu), ("Core (Tdie)", 50, SensorGroup.Cpu)));

    [Fact]
    public void CcdsAndCores_OnlyWithoutPackage()
    {
        Assert.Equal(64, Cpu(("CCD1 (Tdie)", 58, SensorGroup.Cpu), ("CCD2 (Tdie)", 64, SensorGroup.Cpu), ("SoC", 70, SensorGroup.Cpu)));
        Assert.Equal(48, Cpu(("Core #1", 45, SensorGroup.Cpu), ("Core #2", 48, SensorGroup.Cpu)));
        Assert.Equal(52, Cpu(("CPU Cores", 52, SensorGroup.Cpu)));
    }

    [Fact]
    public void ThermalZone_OnlyWhenTheCpuHasNoSensor()
    {
        Assert.Equal(45, Cpu(("CPU Package", 45, SensorGroup.Cpu), ("Thermal zone TZ00", 80, SensorGroup.Board)));
        Assert.Equal(80, Cpu(("Thermal zone TZ00", 80, SensorGroup.Board), ("Thermal zone TZ01", 30, SensorGroup.Board)));
    }

    [Fact]
    public void SeveralCpus_TakeTheHottestPackage()
        => Assert.Equal(66, Cpu(("CPU Package", 61, SensorGroup.Cpu), ("CPU Package", 66, SensorGroup.Cpu), ("CPU Core #1", 80, SensorGroup.Cpu)));
}
