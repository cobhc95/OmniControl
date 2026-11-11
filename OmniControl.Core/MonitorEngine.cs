using OmniControl.Core.Engine;
using OmniControl.Core.Interfaces;

namespace OmniControl.Core;

/// <summary>Facade that owns the monitor, laptop backlight and system volume controllers.</summary>
public sealed class MonitorEngine : IMonitorEngine
{
    public List<IMonitor> Monitors { get; private set; } = new();
    public ILaptopBrightness Laptop { get; }
    public ISystemVolume System { get; }

    private readonly IMonitorEnumerator _enumerator;
    private bool _disposed;

    public MonitorEngine(
        IMonitorEnumerator? enumerator = null,
        ILaptopBrightness? laptop = null,
        ISystemVolume? system = null)
    {
        _enumerator = enumerator ?? new MonitorEnumerator();
        Laptop = laptop ?? new LaptopBrightnessController();
        System = system ?? new SystemVolumeController();
        RefreshMonitors();
    }

    public void RefreshMonitors()
    {
        foreach (var m in Monitors) m.Dispose();
        Monitors = _enumerator.Enumerate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var m in Monitors) m.Dispose();
        GC.SuppressFinalize(this);
    }
}
