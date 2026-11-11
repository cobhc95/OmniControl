using OmniDial.Core.Command;
using OmniDial.Core.Engine;
using OmniDial.Core.Interfaces;
using OmniDial.Core.Models;

namespace OmniDial.Core;

/// <summary>
/// Facade that owns all peripheral controllers. GUI / hotkeys / timers / rules all
/// funnel through here (and ultimately through CommandLanguage).
/// </summary>
public sealed class MonitorEngine : IMonitorEngine
{
    public List<IMonitor> Monitors { get; private set; } = new();
    public ILaptopBrightness Laptop { get; }
    public ISystemVolume System { get; }
    public IGpuSaturation Gpu { get; }
    public CommandLanguage Commands { get; private set; } = null!;

    private readonly IMonitorEnumerator _enumerator;
    private bool _disposed;

    public MonitorEngine(
        IMonitorEnumerator? enumerator = null,
        ILaptopBrightness? laptop = null,
        ISystemVolume? system = null,
        IGpuSaturation? gpu = null)
    {
        _enumerator = enumerator ?? new MonitorEnumerator();
        Laptop = laptop ?? new LaptopBrightnessController();
        System = system ?? new SystemVolumeController();
        Gpu = gpu ?? GpuSaturationFactory.Create();
        RefreshMonitors();
    }

    public void RefreshMonitors()
    {
        foreach (var m in Monitors) m.Dispose();
        Monitors = _enumerator.Enumerate();
        Commands = new CommandLanguage(Monitors, Laptop, System, Gpu);
    }

    public OperationResult ExecuteCommand(string command, Action<string>? log = null)
        => Commands.Execute(command, log);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var m in Monitors) m.Dispose();
        GC.SuppressFinalize(this);
    }
}
