using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Resources.Pages;

/// THE network adapter chip and its menu (the adapters by connection, VPN and not connected). Without a reading it is the Download and Upload cards' chip (Auto,
/// all together, each adapter); one choice drives cards, states and gadget. With a speed reading it is that reading's own chip on the Taskbar gadget tab (Auto =
/// the common choice, then each adapter), <see cref="ResourcesSetup.SpeedAdapterOf"/>; an adapter that is gone reads as Auto.
public sealed class NetworkAdapterMenu
{
    public const string Title = "Network adapter (Download and Upload)", ReadingTitle = "Network adapter", AutoBusiest = "Auto (busiest)", AllCombined = "All adapters combined";

    private readonly IReadingList _monitor;
    private readonly ResourcesSettings _settings;
    private readonly Reading? _reading;
    private bool _showOffline;

    /// <param name="reading">The speed reading this chip is for; null: the common choice of the Download and Upload cards.</param>
    public NetworkAdapterMenu(IReadingList monitor, ResourcesSettings settings, Reading? reading = null)
    {
        (_monitor, _settings, _reading) = (monitor, settings, reading);
        Item = new MenuChoiceItem(ReadingTitle, Words, Rows)
        {
            OpenChanged = open => { if (!open) _showOffline = false; _monitor.ListAdapters(open); },
            Tip = reading is null ? null : $"{reading.Name}: the network adapter it is read from; Auto is the choice on the Readings tab",
        };
    }

    /// The card item both cards show (its chip text and rows follow <see cref="Refresh"/>).
    public MenuChoiceItem Item { get; }

    public void Refresh() => Item.Refresh();

    private string? Choice => _reading is null ? _settings.Setup.Adapter : _settings.Setup.SpeedAdapterOf(_reading.Id);

    private string Words()
    {
        var sample = _monitor.Latest;
        if (_reading is not null)
            return Choice is null ? "Auto" : sample.Adapters.FirstOrDefault(a => a.Id == Choice)?.Name ?? _reading.OwnAdapter(sample) ?? "Auto";
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
        var rows = _reading is not null
            ? new List<MenuRowItem> { Row("Auto", auto, null) }
            : new List<MenuRowItem>
            {
                Row(busiest is null ? AutoBusiest : $"{AutoBusiest} · now {busiest}", auto, null),
                Row(AllCombined, Choice == AdapterSpeed.All, AdapterSpeed.All),
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
        if (Choice == choice) return;
        if (_reading is null) _settings.Setup.Adapter = choice;
        else _settings.Setup.SetSpeedAdapter(_reading.Id, choice);
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
