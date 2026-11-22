using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Traffic;

/// THE reader of the programs' network traffic: one worker timer while the Network Traffic Control page's list is on screen, the
/// per-program session where the network work runs started for it and given back when nobody looks.
public sealed class TrafficMonitor : IDisposable
{
    private readonly Func<ITrafficSession> _start;
    private readonly Dictionary<string, string> _seen = new(); // program id -> its path, every program seen with traffic this session
    private readonly object _work = new(); // one read at a time, and none while disposing
    private readonly Timer _timer;
    private ITrafficSession? _session;
    private volatile bool _wanted;
    private bool _disposed;
    private DateTime _lastTake;

    /// <param name="start">Starts the per-program traffic session where the network work runs (a worker; throws an <see cref="IOException"/> saying why).</param>
    public TrafficMonitor(Func<ITrafficSession> start)
    {
        _start = start;
        _timer = new Timer(_ => Tick());
    }

    /// The programs' traffic of the latest read in bytes per second (empty while nobody reads). UI thread.
    public IReadOnlyList<AppTraffic> Apps { get; private set; } = Array.Empty<AppTraffic>();

    /// Why the per-program traffic cannot be read (one sentence), or null.
    public string? Problem { get; private set; }

    /// Every program seen with traffic since OmniControl started (id and path), in the order first seen (UI thread).
    public IReadOnlyDictionary<string, string> Seen => _seen;

    /// A read arrived (UI thread).
    public event Action? Updated;

    /// Reads every <paramref name="every"/>, or (null) stops and gives the session back. UI thread.
    public void Need(TimeSpan? every)
    {
        _wanted = every is not null;
        if (!_wanted)
        {
            Apps = Array.Empty<AppTraffic>(); // no speeds of a session that stopped
            ThreadPool.QueueUserWorkItem(_ => Release());
        }
        _timer.Change(every is null ? Timeout.InfiniteTimeSpan : TimeSpan.Zero, every ?? Timeout.InfiniteTimeSpan);
    }

    private void Release()
    {
        lock (_work)
        {
            if (_wanted || _disposed) return;
            Guard.Run("Network traffic: release", () =>
            {
                _session?.Dispose();
                _session = null;
            });
        }
    }

    private void Tick()
    {
        if (!Monitor.TryEnter(_work)) return; // the last read is still going: skip this one
        try
        {
            if (_disposed || !_wanted) return;
            Guard.Run("Network traffic read", () =>
            {
                if (Read() is { } apps) Dispatcher.UIThread.Post(() => Arrived(apps));
            });
        }
        finally { Monitor.Exit(_work); }
    }

    /// The programs' bytes per second since the last read, starting the session at the first (worker).
    private IReadOnlyList<AppTraffic>? Read()
    {
        if (_session is null)
        {
            try
            {
                _session = _start();
                Problem = null;
            }
            catch (IOException ex)
            {
                Problem = ex.Message;
                _wanted = false;
                Dispatcher.UIThread.Post(() => Updated?.Invoke());
                return null;
            }
            _lastTake = DateTime.UtcNow;
            return null;
        }
        if (_session.Stopped is { } why)
        {
            _session.Dispose();
            _session = null;
            Problem = why;
            _wanted = false;
            Dispatcher.UIThread.Post(() => Updated?.Invoke());
            return null;
        }
        var seconds = Math.Max((DateTime.UtcNow - _lastTake).TotalSeconds, double.Epsilon);
        _lastTake = DateTime.UtcNow;
        return _session.Take().Select(a => a with { Received = (long)(a.Received / seconds), Sent = (long)(a.Sent / seconds) }).ToList();
    }

    private void Arrived(IReadOnlyList<AppTraffic> apps)
    {
        if (_disposed || !_wanted) return;
        Apps = apps;
        foreach (var app in apps) _seen.TryAdd(app.Id, app.Path);
        Updated?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_work)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
    }
}
