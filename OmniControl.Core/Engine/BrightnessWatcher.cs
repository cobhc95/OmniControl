using System.Management;

namespace OmniControl.Core.Engine;

/// Raises <see cref="Changed"/> (percent, on a worker thread) when the laptop brightness changes, e.g. from the Fn keys.
/// The first read (after <c>firstPoll</c>) decides: no laptop panel answers, and nothing more is done (no event subscription, no poll);
/// a panel answers, and Windows' brightness event is listened to, plus a read as a check because some drivers don't raise the event
/// reliably: every <c>checkPoll</c> for <c>checkFor</c> (or until the event is seen to work). When the event cannot be listened to,
/// it is read every <c>poll</c>. Each read is a WMI query, and failing ones throw, so nothing is kept up without a panel.
public sealed class BrightnessWatcher : IDisposable
{
    private readonly TimeSpan _firstPoll, _poll, _checkPoll, _checkFor;
    private readonly LaptopBrightnessController _laptop = new();
    private readonly object _gate = new();
    private readonly Stopwatch _since = new();
    private ManagementEventWatcher? _watcher;
    private Timer? _timer;
    private int _last = -1;
    private bool _polled, _disposed;

    public event Action<int>? Changed;

    /// <param name="firstPoll">Delay before the first read, so it does not add to the start-up work.</param>
    /// <param name="poll">How often the brightness is read when the event cannot be listened to.</param>
    /// <param name="checkPoll">How often it is read, as a check, while the event is listened to but not yet seen working.</param>
    /// <param name="checkFor">How long that check goes on.</param>
    public BrightnessWatcher(TimeSpan firstPoll, TimeSpan poll, TimeSpan checkPoll, TimeSpan checkFor) =>
        (_firstPoll, _poll, _checkPoll, _checkFor) = (firstPoll, poll, checkPoll, checkFor);

    public void Start()
    {
        _since.Start();
        _timer = new Timer(_ => Poll(), null, _firstPoll, _checkPoll);
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
        if (first)
        {
            if (percent is null) StopPolling(); // nothing to watch on this machine (no laptop panel)
            else if (!Watch())
            {
                try
                {
                    Volatile.Read(ref _timer)?.Change(_poll, _poll);
                }
                catch (ObjectDisposedException)
                {
                    // Stopped meanwhile (Dispose): nothing left to poll.
                }
            }
        }
        else if (_watcher is not null && _since.Elapsed >= _checkFor) StopPolling(); // the event alone from now on
    }

    // Listens to Windows' brightness event; false when it cannot be listened to (polling covers it).
    private bool Watch()
    {
        lock (_gate)
        {
            if (_disposed) return true;
            var watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += (_, e) =>
            {
                StopPolling(); // the event works on this machine: the check is not needed
                using var arrived = e.NewEvent;
                if (arrived["Brightness"] is { } b) Report(Convert.ToInt32(b));
            };
            try
            {
                watcher.Start();
            }
            catch (ManagementException)
            {
                watcher.Dispose();
                return false;
            }
            _watcher = watcher;
            return true;
        }
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
        lock (_gate) _disposed = true;
        StopWatching();
    }
}
