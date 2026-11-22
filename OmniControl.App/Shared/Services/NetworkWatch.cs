namespace OmniControl.App.Shared.Services;

/// What a feature needs of THE network watch: the PC's adapters, the networks Windows has saved, which one each adapter is on and whether
/// it is metered (<see cref="NetworkList"/>), kept fresh only while someone needs it.
public interface INetworkWatch
{
    /// The last reading (empty until the first one after <see cref="Need"/>).
    NetworkSnapshot Now { get; }

    /// <see cref="Now"/> changed (a worker).
    event Action? Changed;

    /// Keeps the watch running until the handle is disposed (UI thread).
    IDisposable Need();
}

/// THE network watch: one hardware watch ("networks") read again on Windows' Network List Manager announcements, an adapter's addresses
/// changing, a device change and a wake, never by polling; registered only while held, so Windows' network list is heard only then.
public sealed class NetworkWatch : INetworkWatch
{
    private const string WatchId = "networks";

    private readonly IHardwareWatches _hardware;
    private readonly HoldCount _needs = new();
    private volatile NetworkSnapshot _now = NetworkSnapshot.Empty;

    public NetworkWatch(IHardwareWatches hardware)
    {
        _hardware = hardware;
        _needs.Changed += Follow;
    }

    public NetworkSnapshot Now => _now;

    public event Action? Changed;

    public IDisposable Need() => _needs.Take();

    private void Follow()
    {
        if (!_needs.Held)
        {
            _hardware.Unregister(WatchId);
            return;
        }
        _hardware.Register(new HardwareWatch(WatchId, "Networks and adapters",
            HardwareSignal.NetworkList | HardwareSignal.Network | HardwareSignal.Device | HardwareSignal.Resume, Refresh,
            Describe: () => $"{_now.Adapters.Count} adapters, {_now.Networks.Count} saved networks"));
        _hardware.Request(WatchId);
    }

    private void Refresh()
    {
        var next = NetworkList.Read();
        if (next.Same(_now)) return;
        _now = next;
        Changed?.Invoke();
    }
}
