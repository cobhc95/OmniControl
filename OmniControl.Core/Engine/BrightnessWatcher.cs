using System.Management;

namespace OmniControl.Core.Engine;

/// Raises <see cref="Changed"/> (percent, on a worker thread) when the laptop brightness changes, e.g. from the Fn keys.
/// Uses the WMI brightness event, plus a read now and then as a check because some drivers don't raise the event reliably: while the
/// event is listened to, only every <c>checkPoll</c> and for <c>checkFor</c> (or until the event is seen to work); when it cannot be
/// listened to, every <c>poll</c>. Reading is never kept up when the first read finds no laptop panel: each read is a WMI query, and
/// failing ones throw, so a pointless poll was steady work and garbage in the background.
public sealed class BrightnessWatcher : IDisposable
{
    private readonly TimeSpan _firstPoll, _poll, _checkPoll, _checkFor;
    private readonly LaptopBrightnessController _laptop = new();
    private readonly object _gate = new();
    private readonly Stopwatch _since = new();
    private ManagementEventWatcher? _watcher;
    private Timer? _timer;
    private int _last = -1;
    private bool _polled;

    public event Action<int>? Changed;

    /// <param name="firstPoll">Delay before the first read, so it does not add to the start-up work.</param>
    /// <param name="poll">How often the brightness is read when the event cannot be listened to.</param>
    /// <param name="checkPoll">How often it is read, as a check, while the event is listened to but not yet seen working.</param>
    /// <param name="checkFor">How long that check goes on.</param>
    public BrightnessWatcher(TimeSpan firstPoll, TimeSpan poll, TimeSpan checkPoll, TimeSpan checkFor) =>
        (_firstPoll, _poll, _checkPoll, _checkFor) = (firstPoll, poll, checkPoll, checkFor);

    public void Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                StopPolling(); // the event works on this machine: the check is not needed
                using var arrived = e.NewEvent;
                if (arrived["Brightness"] is { } b) Report(Convert.ToInt32(b));
            };
            _watcher.Start();
        }
        catch (ManagementException)
        {
            _watcher?.Dispose();
            _watcher = null; // WMI brightness events unavailable: polling covers it
        }

        _since.Start();
        _timer = new Timer(_ => Poll(), null, _firstPoll, _watcher is null ? _poll : _checkPoll);
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
        if (percent is null && first)
        {
            StopPolling(); // nothing to watch on this machine (no laptop panel): the event subscription goes too
            StopWatching();
        }
        else if (_watcher is not null && _since.Elapsed >= _checkFor) StopPolling(); // the event alone from now on
    }

    private void StopPolling() => Interlocked.Exchange(ref _timer, null)?.Dispose();

    private void StopWatching()
    {
        if (Interlocked.Exchange(ref _watcher, null) is not { } watcher) return;
        try
        {
            watcher.Stop();
        }
        catch (ManagementException)
        {
            // Already gone (WMI restarted): nothing more to stop.
        }
        watcher.Dispose();
    }

    /// Raises Changed only when the value differs from the last one seen (event and poll both feed this).
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
        StopWatching();
    }
}
