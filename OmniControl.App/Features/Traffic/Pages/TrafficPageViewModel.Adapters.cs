using OmniControl.App.Features.Resources;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Traffic.Pages;

// The header's "Adapter: …" choice: All adapters (the base rules), each adapter (live from Windows, or kept for its own rules while gone) and,
// beside an adapter with saved networks, a side list of them. The table, the title's note and the line under the header follow the choice.
public sealed partial class TrafficPageViewModel
{
    internal const string AdapterTitle = "Adapter", AllAdapters = "All adapters", BaseWords = "base settings", OwnWords = "own settings",
        UsesAllWords = "uses All", NotConnected = "not connected", FindNetwork = "Find a network…";

    private NetScope _scope = NetScope.All;
    private string _networkFind = string.Empty;

    /// The header's pull-down (one choice).
    public MenuChoiceItem AdapterChoice { get; }

    /// The line under the header while one adapter or network is chosen, and its link back to the parent's rules.
    public string PlaceLine { get; private set; } = string.Empty;
    public bool HasPlace => !_scope.IsAll;
    public string UseParentWords { get; private set; } = string.Empty;
    public bool CanUseParent => _limits.HasOwn(_scope);
    public IRelayCommand UseParentCommand { get; }

    /// The line under it while the place's automation rule has more than the page shows.
    public RuleLineItem PlaceRule => _placeRule ??= new RuleLineItem(() => _limits.EditRule(_scope));

    private RuleLineItem? _placeRule;

    private NetworkSnapshot Networks => _networks.Now;

    private MenuChoiceItem MakeAdapterChoice() => new(AdapterTitle, AdapterWords, AdapterRows)
    {
        Tip = "Whose rules the table shows and edits: All adapters', or one adapter's or network's own (the most specific wins)",
        OpenChanged = open => { if (!open) _networkFind = string.Empty; },
    };

    private string AdapterWords() => _scope.IsAll ? "Adapter: all"
        : _scope.IsNetwork ? $"Adapter: {AdapterName(_scope.Adapter)} · {NetworkName(_scope.Network)}" : $"Adapter: {AdapterName(_scope.Adapter)}";

    /// All adapters, a thin line, then every adapter: live ones, and gone ones that keep rules of their own.
    private IReadOnlyList<MenuRowItem> AdapterRows()
    {
        var rows = new List<MenuRowItem> { new(AllAdapters, string.Empty, _scope.IsAll, string.Empty, ChooseCommand(NetScope.All), Trailing: BaseWords) };
        var first = true;
        foreach (var (id, name, kind) in AdapterList())
        {
            var on = Networks.On(id);
            var trailing = $"{on?.Name ?? NotConnected} · {(_limits.HasOwn(NetScope.Of(id)) ? OwnWords : UsesAllWords)}";
            var place = NetScope.Of(id);
            rows.Add(SavedNetworksOf(id, kind).Count > 0
                ? new MenuRowItem(name, string.Empty, _scope.Adapter == id, string.Empty, NoChoice, Trailing: trailing, RuleAbove: first, Submenu: () => NetworkRows(id, kind))
                : new MenuRowItem(name, string.Empty, _scope == place, string.Empty, ChooseCommand(place), Trailing: trailing, RuleAbove: first));
            first = false;
        }
        return rows;
    }

    /// Every adapter Windows has now, then those gone that keep rules of their own (by name).
    private IEnumerable<(string Id, string Name, AdapterKind Kind)> AdapterList() =>
        Networks.Adapters.Select(a => (a.Id, a.Name, a.Kind))
            .Concat(_settings.Setup.Scoped.Where(s => Networks.Adapters.All(a => a.Id != s.Adapter)).GroupBy(s => s.Adapter)
                .Select(g => (g.Key, g.First().AdapterName, AdapterKind.Other)));

    /// The saved networks an adapter may join (Wi-Fi: every saved Wi-Fi network; wired: wired ones, only when there are several), the one it is
    /// on now and those with rules of their own on it.
    private List<(string Id, string Name)> SavedNetworksOf(string adapter, AdapterKind kind)
    {
        var medium = kind switch { AdapterKind.Wifi => NetworkMedium.Wireless, AdapterKind.Ethernet => NetworkMedium.Wired, _ => (NetworkMedium?)null };
        var list = Networks.Networks.Where(n => (medium is not null && n.Medium == medium) || n.Adapters.Contains(adapter)).Select(n => (n.Id, n.Name))
            .Concat(_settings.Setup.Scoped.Where(s => s.Adapter == adapter && s.Network.Length > 0).Select(s => (Id: s.Network, Name: s.NetworkName)))
            .DistinctBy(n => n.Id).ToList();
        return kind == AdapterKind.Wifi || list.Count > 1 ? list : new List<(string, string)>();
    }

    /// The side list: Every <adapter> network, the one connected, those with own rules, then the rest under a find box.
    private IReadOnlyList<MenuRowItem> NetworkRows(string adapter, AdapterKind kind)
    {
        var name = AdapterName(adapter);
        var networks = SavedNetworksOf(adapter, kind);
        var connected = Networks.On(adapter)?.Id;
        var own = networks.Where(n => n.Id != connected && _limits.HasOwn(new NetScope(adapter, n.Id))).ToList();
        var rest = networks.Where(n => n.Id != connected && own.All(o => o.Id != n.Id)).ToList();
        var shown = rest.Where(n => n.Name.Contains(_networkFind, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var rows = new List<MenuRowItem> { new($"Every {name} network", string.Empty, _scope == NetScope.Of(adapter), string.Empty, ChooseCommand(NetScope.Of(adapter))) };
        MenuRowItem Network((string Id, string Name) n, bool dim = false) =>
            new(n.Name, string.Empty, _scope == new NetScope(adapter, n.Id), string.Empty, ChooseCommand(new NetScope(adapter, n.Id)), Dim: dim);
        if (networks.FirstOrDefault(n => n.Id == connected) is { Id: not null } now)
        {
            rows.Add(MenuRowItem.Header("Connected", ruleAbove: true));
            rows.Add(Network(now));
        }
        if (own.Count > 0)
        {
            rows.Add(MenuRowItem.Header("Own settings", ruleAbove: connected is null));
            rows.AddRange(own.Select(n => Network(n)));
        }
        if (rest.Count == 0) return rows;
        rows.Add(new MenuRowItem(FindNetwork, string.Empty, false, string.Empty, NoChoice, RuleAbove: true, Trailing: $"{rest.Count} more",
            Find: text => _networkFind = text));
        rows.AddRange(shown.Select(n => Network(n, dim: true)));
        return rows;
    }

    private static readonly IRelayCommand NoChoice = new RelayCommand(() => { });

    private IRelayCommand ChooseCommand(NetScope place) => new RelayCommand(() => Choose(place));

    private void Choose(NetScope place)
    {
        if (place == _scope) return;
        _scope = place;
        ShowPlace();
        foreach (var row in AppRows) row.Load();
        ShowTraffic();
    }

    /// The pill, the title's note and the line under the header say which rules the table shows.
    private void ShowPlace()
    {
        AdapterChoice.Refresh();
        Apps.NameNote = _scope.IsAll ? string.Empty : $"· {(_scope.IsNetwork ? NetworkName(_scope.Network) : AdapterName(_scope.Adapter))} only";
        var adapter = AdapterName(_scope.Adapter);
        (PlaceLine, UseParentWords) = _scope.IsAll ? (string.Empty, string.Empty)
            : _scope.IsNetwork ? ($"Changes here apply only while connected to {NetworkName(_scope.Network)} ({adapter}).", $"Use {adapter}'s settings")
            : ($"Changes here apply only on {adapter} ({Networks.On(_scope.Adapter)?.Name ?? NotConnected}).", "Use All adapters' settings");
        var rule = _limits.HasOwn(_scope) ? _limits.RuleOf(_scope) : null;
        if (_limits.HasOwn(_scope) && rule is not { On: true }) PlaceLine += " Its automation rule is off, so they are not used.";
        PlaceRule.Show(rule);
        OnPropertyChanged(nameof(PlaceLine));
        OnPropertyChanged(nameof(HasPlace));
        OnPropertyChanged(nameof(UseParentWords));
        OnPropertyChanged(nameof(CanUseParent));
    }

    private string AdapterName(string id) => Networks.Adapters.FirstOrDefault(a => a.Id == id)?.Name
        ?? _settings.Setup.Scoped.FirstOrDefault(s => s.Adapter == id)?.AdapterName ?? id;

    private string NetworkName(string id) => Networks.Networks.FirstOrDefault(n => n.Id == id)?.Name
        ?? _settings.Setup.Scoped.FirstOrDefault(s => s.Network == id)?.NetworkName ?? id;

    /// The adapters or their networks changed (a worker): the pull-down and the line follow.
    private void OnNetworksChanged() => Dispatcher.UIThread.Post(() => Guard.Run("Network traffic control: follow the networks", ShowPlace));
}
