using OmniControl.Core.Engine;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.Core;

/// <summary>Facade that owns the monitor and laptop backlight controllers.</summary>
public sealed class MonitorEngine : IMonitorEngine
{
    private readonly IMonitorEnumerator _enumerator;
    private readonly object _refresh = new();   // one refresh at a time: monitors are disposed as they are replaced
    private bool _disposed;

    public IReadOnlyList<IMonitor> Monitors { get; private set; } = Array.Empty<IMonitor>();
    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = Array.Empty<DisplayInfo>();
    public ILaptopBrightness Laptop { get; }

    public MonitorEngine(IMonitorEnumerator? enumerator = null, ILaptopBrightness? laptop = null)
    {
        _enumerator = enumerator ?? new MonitorEnumerator();
        Laptop = laptop ?? new LaptopBrightnessController();
        RefreshMonitors();
    }

    public void RefreshMonitors()
    {
        lock (_refresh)
        {
            if (_disposed) return;
            var old = Monitors;
            Displays = ListDisplays();
            Monitors = _enumerator.Enumerate();
            foreach (var m in old) m.Dispose();
        }
    }

    public bool Rescan()
    {
        lock (_refresh)
        {
            if (_disposed || Signature(ListDisplays()) == Signature(Displays)) return false;
            RefreshMonitors();
            return true;
        }
    }

    private static List<DisplayInfo> ListDisplays() => DisplayEnumerator.Enumerate().Select(d => d.Info).ToList();

    private static string Signature(IEnumerable<DisplayInfo> displays) => string.Join('|', displays.Select(d => d.ToString()));

    public void Dispose()
    {
        lock (_refresh)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var m in Monitors) m.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
