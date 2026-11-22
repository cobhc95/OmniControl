using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Traffic;

/// The network states of the master list, read from THE network watch: "Connected to <network>" (one per saved network), "On a metered
/// connection" and "Not connected". The watch is held while a rule or trigger follows one of them (<see cref="IStateRegistry.Watch"/>),
/// and once at start so the saved networks can be listed.
internal sealed class NetworkStates : IDisposable
{
    private const string IdStart = "network.", Prefix = IdStart + "connected.", MeteredId = IdStart + "metered", OfflineId = IdStart + "none";
    private const string ConnectedHeading = "Connected to", StatusHeading = "Connection", MoreHeading = "More networks";
    private const string HowKnown = "Windows' network list, read when it announces a change";

    private readonly IStateRegistry _states;
    private readonly INetworkWatch _networks;
    private readonly string _group;
    private IDisposable? _first, _followed;
    private string _listed = string.Empty;

    public NetworkStates(IStateRegistry states, INetworkWatch networks, string group)
    {
        (_states, _networks, _group) = (states, networks, group);
        _networks.Changed += OnChanged;
        _states.WatchedChanged += Follow;
        _first = _networks.Need();
        List();
    }

    private NetworkSnapshot Now => _networks.Now;

    private bool Metered => Now.Networks.Any(n => n.Connected && n.Metered);

    private bool Offline => !Now.Networks.Any(n => n.Connected);

    // A worker: the values are read first, then the list follows (both posted in that order, so a re-registration never hides a change).
    private void OnChanged()
    {
        _states.Notify(_states.States.Where(s => s.Id.StartsWith(IdStart, StringComparison.Ordinal)).Select(s => s.Id).ToList());
        Dispatcher.UIThread.Post(() => Guard.Run("Network states: follow the networks", () =>
        {
            _first?.Dispose();
            _first = null;
            Follow();
        }));
    }

    /// Registers the states again only when what the picker shows changed (a network saved, renamed, connected, or used by a rule).
    private void List()
    {
        var networks = Now.Networks;
        var rows = networks.Select(n => (n, Heading: n.Connected || _states.IsWatched(Prefix + n.Id) ? ConnectedHeading : MoreHeading))
            .OrderBy(r => r.n.Connected ? 0 : 1).ToList();
        var key = string.Join("|", rows.Select(r => $"{r.n.Id},{r.n.Name},{r.n.Medium},{r.Heading}"));
        if (key == _listed) return;
        _listed = key;
        _states.Register(rows.Select(r => new StateEntry(Prefix + r.n.Id, $"Connected to {r.n.Name}", _group, StateKind.OnOff,
                () => StateValue.Of(Now.Networks.Any(n => n.Id == r.n.Id && n.Connected)), HowKnown,
                Heading: r.Heading, Keywords: "network wifi wi-fi wired ethernet connected", Short: r.n.Name, Note: KindOf(r.n.Medium)))
            .Append(new StateEntry(MeteredId, "On a metered connection", _group, StateKind.OnOff, () => StateValue.Of(Metered), HowKnown,
                Heading: StatusHeading, Keywords: "network metered data limit cost", Short: "Metered connection"))
            .Append(new StateEntry(OfflineId, "Not connected", _group, StateKind.OnOff, () => StateValue.Of(Offline), HowKnown,
                Heading: StatusHeading, Keywords: "network offline disconnected internet", Short: "Not connected")));
    }

    private static string? KindOf(NetworkMedium medium) => medium switch
    {
        NetworkMedium.Wireless => "Wi-Fi",
        NetworkMedium.Wired => "wired",
        NetworkMedium.Vpn => "VPN",
        NetworkMedium.Mobile => "mobile",
        _ => null,
    };

    /// The watch runs while a rule or trigger follows one of these states (UI thread).
    private void Follow()
    {
        var needed = _states.States.Any(s => s.Id.StartsWith(IdStart, StringComparison.Ordinal) && _states.IsWatched(s.Id));
        if (needed && _followed is null) _followed = _networks.Need();
        else if (!needed && _followed is not null)
        {
            _followed.Dispose();
            _followed = null;
        }
        List();
    }

    public void Dispose()
    {
        _networks.Changed -= OnChanged;
        _states.WatchedChanged -= Follow;
        _first?.Dispose();
        _followed?.Dispose();
    }
}
