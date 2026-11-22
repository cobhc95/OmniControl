namespace OmniControl.Core.Engine.Resources;

/// A running per-program traffic session: <see cref="Take"/> hands over each program's bytes since the last take.
public interface ITrafficSession : IDisposable
{
    /// Null while traffic is reported; one plain sentence once it stopped on its own.
    string? Stopped { get; }

    IReadOnlyList<AppTraffic> Take();
}

/// Where the per-program network work that needs administrator rights runs: in OmniControl itself (<see cref="NetworkWork"/>, while it runs
/// as administrator) or in the network service (the app's client of it). Workers only.
public interface INetworkWork
{
    /// Makes Windows hold exactly <paramref name="wanted"/> (upload caps, blocks, download limits) and reads it back; one sentence per problem.
    IReadOnlyList<string> Sync(IReadOnlyList<NetworkRule> wanted);

    /// Windows' adapters may have changed: running download limits pick up a new one.
    void RefreshAdapters();

    /// Shut-down, first: download limits end and nothing starts them again.
    void StopDownloads();

    /// Takes every rule out of Windows (shut-down, or the work moving elsewhere); returns the failures.
    IReadOnlyList<string> RemoveAll();

    /// Starts reading traffic per program; throws an <see cref="IOException"/> saying why in one plain sentence when it cannot.
    ITrafficSession StartTraffic();
}

/// The privileged network work itself, used by OmniControl while it runs as administrator and by the network service: upload caps and
/// blocks through <see cref="NetworkRules"/>, download limits through <see cref="DownloadShaper"/> (run only while one is set and Windows
/// Packet Filter is installed; a shaper that ended is started again by the next sync), traffic through <see cref="NetworkByApp"/>.
public sealed class NetworkWork : INetworkWork
{
    private readonly NetworkRules _rules = new();
    private readonly ShapingTimes _times;
    private readonly Action<string> _downloadFailed;
    private readonly object _gate = new();
    private DownloadShaper? _shaper;
    private bool _stopped;

    /// <param name="downloadFailed">Told on the packet thread, in one plain sentence, when download shaping ends by itself.</param>
    public NetworkWork(ShapingTimes times, Action<string> downloadFailed) => (_times, _downloadFailed) = (times, downloadFailed);

    public IReadOnlyList<string> Sync(IReadOnlyList<NetworkRule> wanted)
    {
        lock (_gate)
        {
            Shape(wanted.Where(r => r.DownloadBlocked || r.DownloadBytes > 0)
                .Select(r => new DownloadLimit(r.AppId, r.DownloadBlocked ? DownloadLimit.Block : r.DownloadBytes!.Value, r.Adapter)).ToList());
            return _rules.Sync(wanted.Where(r => r.NeedsRules).ToList());
        }
    }

    private void Shape(IReadOnlyList<DownloadLimit> limits)
    {
        var run = !_stopped && limits.Count > 0 && DownloadShaper.Installed;
        if (!run || _shaper is { Running: false }) StopShaper();
        if (!run) return;
        _shaper ??= DownloadShaper.Start(_times, _downloadFailed);
        _shaper.SetLimits(limits);
    }

    private void StopShaper()
    {
        _shaper?.Dispose();
        _shaper = null;
    }

    public void RefreshAdapters()
    {
        lock (_gate) _shaper?.RefreshAdapters();
    }

    public void StopDownloads()
    {
        lock (_gate)
        {
            _stopped = true;
            StopShaper();
        }
    }

    /// Skipped when the last sync proved none is held.
    public IReadOnlyList<string> RemoveAll()
    {
        lock (_gate) return _rules.HoldsNone ? Array.Empty<string>() : _rules.RemoveAll();
    }

    public ITrafficSession StartTraffic()
    {
        try { return NetworkByApp.Start(); }
        catch (Win32Exception ex) { throw new IOException($"Windows won't report traffic per program: {ex.Message}", ex); }
    }
}
