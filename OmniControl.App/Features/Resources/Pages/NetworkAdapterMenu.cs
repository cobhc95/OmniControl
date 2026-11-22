using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// The network adapter chip of the Download and Upload cards and its menu (Auto, all together, each adapter); one choice drives cards, states and gadget.
public sealed class NetworkAdapterMenu
{
    public const string Title = "Network adapter (Download and Upload)";

    private readonly IReadingList _monitor;
    private readonly ResourcesSettings _settings;
    private bool _showOffline;

    /// <param name="listing">The menu opened (true) or closed: the sampler lists the adapters only while it is open.</param>
    public NetworkAdapterMenu(IReadingList monitor, ResourcesSettings settings, Action<bool> listing)
    {
        (_monitor, _settings) = (monitor, settings);
        Item = new MenuChoiceItem("Network adapter", Words, Rows) { OpenChanged = open => { if (!open) _showOffline = false; listing(open); } };
    }

    /// The card item both cards show (its chip text and rows follow <see cref="Refresh"/>).
    public MenuChoiceItem Item { get; }

    public void Refresh() => Item.Refresh();

    private string? Choice => _settings.Setup.Adapter;

    private string Words()
    {
        var sample = _monitor.Latest;
        if (Choice == AdapterSpeed.All) return "All adapters";
        if (!sample.AdapterIsAuto) return sample.Adapter ?? "Auto";
        return sample.Adapter is { } name ? $"Auto · {name}" : "Auto";
    }

    private IReadOnlyList<MenuRowItem> Rows()
    {
        var sample = _monitor.Latest;
        var busiest = sample.Adapters.Where(a => a.IsUp).MaxBy(a => a.Download + a.Upload)?.Name;
        // A stored adapter that is gone reads as Auto, as the sampler does.
        var auto = Choice is null || (Choice != AdapterSpeed.All && sample.Adapters.Count > 0 && sample.Adapters.All(a => a.Id != Choice));
        var rows = new List<MenuRowItem>
        {
            Row(busiest is null ? "Auto (busiest)" : $"Auto (busiest) · now {busiest}", auto, null),
            Row("All adapters combined", Choice == AdapterSpeed.All, AdapterSpeed.All),
        };
        var connected = sample.Adapters.Where(a => a.IsUp).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        AddGroup(rows, "CONNECTED", connected.Where(a => a.IsPhysical));
        AddGroup(rows, "VPN AND VIRTUAL", connected.Where(a => !a.IsPhysical));
        var offline = sample.Adapters.Where(a => !a.IsUp).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (offline.Count == 0) return rows;
        rows.Add(new MenuRowItem($"{(_showOffline ? "▾" : "▸")} Not connected ({offline.Count})", string.Empty, false, string.Empty,
            new RelayCommand(() => { _showOffline = !_showOffline; Item.Refresh(); }), KeepsOpen: true, RuleAbove: true));
        if (_showOffline) rows.AddRange(offline.Select(a => AdapterRow(a)));
        return rows;
    }

    private void AddGroup(List<MenuRowItem> rows, string heading, IEnumerable<AdapterSpeed> adapters)
    {
        var list = adapters.ToList();
        if (list.Count == 0) return;
        rows.Add(MenuRowItem.Header(heading, ruleAbove: true));
        rows.AddRange(list.Select(a => AdapterRow(a)));
    }

    private MenuRowItem AdapterRow(AdapterSpeed a) =>
        Row(a.Name, Choice == a.Id, a.Id, Icon(a.Kind), !a.IsUp, a.IsUp ? Speeds(a) : "not connected");

    /// "↓ 21 · ↑ 61 KB/s" (the unit once when both sides share it); "idle" for a quiet virtual or VPN adapter.
    private static string Speeds(AdapterSpeed a)
    {
        if (!a.IsPhysical && a.Download < 1 && a.Upload < 1) return "idle";
        string down = Reading.SpeedWords(a.Download), up = Reading.SpeedWords(a.Upload);
        var space = down.LastIndexOf(' ');
        if (space > 0 && up.EndsWith(down[space..], StringComparison.Ordinal)) down = down[..space];
        return $"↓ {down} · ↑ {up}";
    }

    private MenuRowItem Row(string text, bool on, string? choice, string? icon = null, bool dim = false, string trailing = "") =>
        new(text, string.Empty, on, string.Empty, new RelayCommand(() => Choose(choice)), Dim: dim, Icon: icon, Trailing: trailing);
    private void Choose(string? choice)
    {
        if (_settings.Setup.Adapter == choice) return;
        _settings.Setup.Adapter = choice;
        _settings.Commit();
    }

    private static string Icon(AdapterKind kind) => kind switch
    {
        AdapterKind.Wifi => "wifi",
        AdapterKind.Ethernet => "network",
        AdapterKind.Vpn => "shield-lock",
        _ => "server",
    };
}
