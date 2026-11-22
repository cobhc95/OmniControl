using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic.Pages;

// What the Network Traffic Control page offers to global search, defined once (see SearchBuilder): the Packet filter switch, the Adapter
// choice, the presets, the All apps row, the limit and block menus, the status line and install links, and one result per program row, each
// with its live row when the page is made.
public sealed partial class TrafficPageViewModel
{
    private const string TrafficWords = "network traffic internet upload download speed limit throttle block firewall bandwidth app program";

    private const string RestoreTipText = "Turns Windows Packet Filter back on.";

    private static readonly SearchCard FilterCard = new("Packet filter",
            "packet filter windows packet filter winpkfilter ndisrd driver network adapter download limit conflict vpn tailscale off", PageTitle)
        {
            Tip = "Download limits use Windows Packet Filter. Turn it off if another program has network trouble.",
        },
        GroupCard = new(PageTitle, TrafficWords + " restore defaults", PageTitle) { Tip = RestoreTipText },
        LimitsCard = new("Upload and download limits", TrafficWords + " kilobytes megabytes per second", PageTitle)
        {
            Menu = AppNetItem.LimitWords,
            Tip = $"{NetworkLimits.UploadBlockTip} {NetworkLimits.DownloadBlockTip}",
        },
        AdapterCard = new(AdapterTitle, TrafficWords + " adapter adapters wifi wi-fi ethernet vpn network networks saved per adapter per network", PageTitle)
        {
            Menu = new[] { AllAdapters, BaseWords, OwnWords, UsesAllWords, NotConnected, "Every network", "Connected", "Own settings", FindNetwork,
                "Use All adapters' settings", "Changes here apply only on one adapter or network" },
            Tip = "Whose rules the table shows and edits: All adapters', or one adapter's or network's own (the most specific wins)",
        },
        AllAppsCard = new(AllAppsItem.Name, TrafficWords + " entire pc every app all programs mixed whole", PageTitle)
        {
            Note = AllAppsItem.Detail,
            Choices = AppNetItem.LimitWords.Append(AllAppsItem.MixedWord).ToList(),
        },
        PresetsCard = new($"{PageTitle} presets", TrafficWords + " preset presets", PageTitle)
        {
            Choices = new[] { TrafficPresets.NormalName, ProviderChoice.CustomText },
            Menu = new[] { ProviderChoice.ManageText },
        },
        InstallCard = new("Install network service", TrafficWords + " install windows packet filter driver service administrator", PageTitle) { Tip = NetworkLimits.InstallServiceTip },
        ColumnsCard = new("Program columns", TrafficWords + " app down up upload limit download limit on pin unpin open file location copy program path switch", PageTitle);

    private static SearchCard StatusCard(INetworkLimits limits) =>
        new("Network traffic status", TrafficWords + " status service packet filter throttle firewall", PageTitle) { Note = $"{limits.Service.Line} {limits.Download.Line}" };

    private static SearchCard AppCard(string name, string id) =>
        new(name, $"{TrafficWords} {id}", PageTitle) { Choices = AppNetItem.LimitWords };

    protected override SearchBuilder SearchCards => Cards(_settings, _limits, this);

    /// THE definition of the page's search: the Packet filter switch, the limit menus and status, and a result per program (the page's rows
    /// while it is made; otherwise the programs holding a rule or a pin).
    private static SearchBuilder Cards(ResourcesSettings? settings, INetworkLimits? limits, TrafficPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(FilterCard, page?.PacketFilter).Add(GroupCard).Add(AdapterCard, page?.AdapterChoice).Add(PresetsCard, page?.PresetChoice)
            .Add(AllAppsCard).Add(LimitsCard).Add(InstallCard).Add(ColumnsCard);
        if (limits is not null) builder.Add(StatusCard(limits));
        if (page is not null) return builder.AddEach(page.AppRows, r => AppCard(r.Name, r.Id), r => r);
        var ids = (settings?.Setup.Apps.Select(r => r.Id) ?? Enumerable.Empty<string>()).Concat((settings?.Setup.Pinned ?? new List<string>()).Select(ProgramIdentity.Normalize)).Distinct();
        return builder.AddEach(ids, id => AppCard(id, id));
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(ResourcesSettings? settings, INetworkLimits? limits) => Cards(settings, limits, null).Words;
}
