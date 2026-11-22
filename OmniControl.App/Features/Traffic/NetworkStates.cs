using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Traffic;

/// The network states of the master list, read from THE network watch: "Connected to <network>" (one per saved network), "On a metered
/// connection", "Not connected" and "<adapter> connected" (one per adapter, behind the find box). The watch is held while a rule or trigger follows one of them (<see cref="IStateRegistry.Watch"/>),
/// and once at start so the saved networks can be listed.
internal sealed class NetworkStates : IDisposable
{
    private const string IdStart = "network.", Prefix = IdStart + "connected.", AdapterPrefix = IdStart + "adapter.", MeteredId = IdStart + "metered", OfflineId = IdStart + "none";

    /// "Connected to <network>".
    public static string ConnectedId(string network) => Prefix + network;

    /// "<adapter> connected".
    public static string AdapterId(string adapter) => AdapterPrefix + adapter;
    private static readonly StateMenu Menu = new("Network", "Connected to", "Find a network…");
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

    /// Registers the states again only when what the side list shows changed (a network saved, renamed, connected, or used by a rule): the
    /// connected network first, then those used by rules, under "Connected to"; metered and not connected; the rest behind the find box.
    private void List()
    {
        var rows = Now.Networks.Select(n => (n, Part: n.Connected || _states.IsWatched(Prefix + n.Id) ? StateMenuPart.Top : StateMenuPart.Rest))
            .OrderBy(r => r.Part).ThenBy(r => r.n.Connected ? 0 : 1).ToList();
        var key = string.Join("|", rows.Select(r => $"{r.n.Id},{r.n.Name},{r.n.Medium},{r.Part}").Concat(Now.Adapters.Select(a => $"{a.Id},{a.Name}")));
        if (key == _listed) return;
        _listed = key;
        var networks = rows.Select(r => new StateEntry(Prefix + r.n.Id, $"Connected to {r.n.Name}", _group, StateKind.OnOff,
            () => StateValue.Of(Now.Networks.Any(n => n.Id == r.n.Id && n.Connected)), HowKnown,
            Keywords: "network wifi wi-fi wired ethernet connected", Short: r.n.Name, Note: KindOf(r.n.Medium), Menu: Menu, MenuPart: r.Part)).ToList();
        // The side list reads "On a metered connection" (ShortName), the chip "Metered connection" (Name).
        var status = new[]
        {
            new StateEntry(MeteredId, "Metered connection", _group, StateKind.OnOff, () => StateValue.Of(Metered), HowKnown,
                Keywords: "network metered data limit cost", Short: "On a metered connection", Menu: Menu, MenuPart: StateMenuPart.Middle),
            new StateEntry(OfflineId, "Not connected", _group, StateKind.OnOff, () => StateValue.Of(Offline), HowKnown,
                Keywords: "network offline disconnected internet", Menu: Menu, MenuPart: StateMenuPart.Middle),
        };
        var adapters = Now.Adapters.Select(a => new StateEntry(AdapterId(a.Id), $"{a.Name} connected", _group, StateKind.OnOff,
            () => StateValue.Of(Now.Adapters.Any(n => n.Id == a.Id && n.IsUp)), HowKnown, Keywords: "network adapter connected wifi ethernet", Short: a.Name, Note: "adapter",
            Menu: Menu, MenuPart: StateMenuPart.Rest));
        _states.Register(networks.Where(s => s.MenuPart == StateMenuPart.Top).Concat(status).Concat(networks.Where(s => s.MenuPart == StateMenuPart.Rest)).Concat(adapters));
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
