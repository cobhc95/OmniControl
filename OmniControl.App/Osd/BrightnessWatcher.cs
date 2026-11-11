using System.Management;
using OmniControl.Core.Engine;

namespace OmniControl.App.Osd;

/// <summary>
/// Raises <see cref="Changed"/> (percent, on a worker thread) when the laptop brightness changes, e.g. from the Fn keys.
/// Uses the WMI brightness event, plus a light poll as a fallback because some drivers don't raise the event reliably.
/// </summary>
public sealed class BrightnessWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private Timer? _poll;
    private readonly LaptopBrightnessController _laptop = new();
    private int _last = -1;
    private readonly object _gate = new();

    public event Action<int>? Changed;

    public void Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                if (e.NewEvent["Brightness"] is { } b) Report(Convert.ToInt32(b));
            };
            _watcher.Start();
        }
        catch { _watcher = null; }

        _poll = new Timer(_ => Poll(), null, 500, 400);
    }

    private void Poll()
    {
        try
        {
            if (_laptop.GetBrightness(out var level, out var max).Success && max > 0) Report(level * 100 / max);
        }
        catch { /* no laptop panel */ }
    }

    /// <summary>Raises Changed only when the value differs from the last one seen (event and poll both feed this).</summary>
    private void Report(int percent)
    {
        lock (_gate)
        {
            var first = _last < 0;
            if (percent == _last) return;
            _last = percent;
            if (first) return;
        }
        Changed?.Invoke(percent);
    }

    public void Dispose()
    {
        _poll?.Dispose();
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
    }
}
