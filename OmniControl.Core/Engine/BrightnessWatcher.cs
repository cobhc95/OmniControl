using OmniControl.Core.Models;
using System.Management;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>
/// Raises <see cref="Changed"/> (percent, on a worker thread) when the laptop brightness changes, e.g. from the Fn keys.
/// Uses the WMI brightness event, plus a poll as a fallback because some drivers don't raise the event reliably. The poll stops
/// once the event has been seen to work, and is never kept up when the first read finds no laptop panel: each read is a WMI
/// query, and failing ones throw, so a pointless poll was steady work and garbage in the background.
/// </summary>
public sealed class BrightnessWatcher : IDisposable
{
    private readonly TimeSpan _firstPoll, _poll;
    private readonly LaptopBrightnessController _laptop = new();
    private readonly object _gate = new();
    private ManagementEventWatcher? _watcher;
    private Timer? _timer;
    private int _last = -1;
    private bool _polled;

    public event Action<int>? Changed;

    /// <param name="firstPoll">Delay before the first fallback read, so it does not add to the start-up work.</param>
    /// <param name="poll">How often the fallback reads the brightness.</param>
    public BrightnessWatcher(TimeSpan firstPoll, TimeSpan poll) => (_firstPoll, _poll) = (firstPoll, poll);

    public void Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                StopPolling(); // the event works on this machine: the fallback is not needed
                if (e.NewEvent["Brightness"] is { } b) Report(Convert.ToInt32(b));
            };
            _watcher.Start();
        }
        catch (ManagementException)
        {
            _watcher = null; // WMI brightness events unavailable: polling covers it
        }

        _timer = new Timer(_ => Poll(), null, _firstPoll, _poll);
    }

    private void Poll()
    {
        int? percent = null;
        try
        {
            if (_laptop.GetBrightness(out var level, out var max).Success && max > 0) percent = Percent.Of(level, max);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            // No laptop panel answers this WMI class (a desktop), or WMI is busy: no reading this time.
        }

        var first = !_polled;
        _polled = true;
        if (percent is { } p) Report(p);
        else if (first) StopPolling(); // nothing to watch on this machine
    }

    private void StopPolling() => Interlocked.Exchange(ref _timer, null)?.Dispose();

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
        StopPolling();
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch (ManagementException) { /* the watcher may already be gone at shutdown */ }
    }
}
