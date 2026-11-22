using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs.Pages;

// What the Other PCs page offers to global search, defined once (see SearchBuilder): the page's parts, the Tailscale checklist, and one result
// per paired PC (its name, what it may do here) that opens the PC's tab.
public sealed partial class OtherPcsPageViewModel
{
    private const string PcWords = "other pcs computers pair paired another pc laptop network lan";

    private static readonly SearchCard PairCard = new("Pair another PC", PcWords + " pairing code new pc plus add link find", OtherPcsService.Category),
        WakeCard = new("Wake this PC", PcWords + " wake sleep shut down wake-on-lan magic packet set up", OtherPcsService.Category),
        TailscaleCard = new("Over the internet", PcWords + " tailscale internet remote route zeroTier vpn checklist firewall", OtherPcsService.Category)
        {
            Choices = TailscaleChecks.Build(TailscaleStatus.NotInstalled, null, null, string.Empty, "PC").Select(c => c.Words).ToList(),
            Tip = string.Join(" ", TailscaleChecks.Build(TailscaleStatus.NotInstalled, null, null, string.Empty).Select(c => $"{c.Sentence} {c.Fix}".Trim()).Where(s => s.Length > 0)),
        },
        AllowancesCard = new("What a paired PC may do here", PcWords + " allow permission switch power actions read settings devices", OtherPcsService.Category)
        {
            Choices = PcAllowances.All.Select(a => a.Label).ToList(),
            Tip = string.Join(" ", PcAllowances.All.Select(a => a.Tip)),
        };

    private static SearchCard PcCard(PairedPc pair) =>
        new(pair.DisplayName, PcWords, OtherPcsService.Category)
        {
            Choices = PcAllowances.All.Select(a => a.Label).ToList(),
            Menu = new[] { PcAllowances.Title(pair.DisplayName) },
            Tip = string.Join(" ", PcAllowances.All.Select(a => a.Tip)),
        };

    protected override SearchBuilder SearchCards => Cards(_service, this);

    /// THE definition of the page's search: its parts and the Tailscale checklist, then one result per paired PC (a fact card made here; its
    /// Open pill selects the PC's tab and shows the page).
    private static SearchBuilder Cards(OtherPcsService? service, OtherPcsPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(PairCard).Add(WakeCard).Add(TailscaleCard).Add(AllowancesCard);
        if (service is null) return builder;
        return builder.AddEach(service.Pairs, PcCard, pair => page?.Pcs.FirstOrDefault(p => p.Id == pair.Id) is { } item ? page.ResultFor(item, PcCard(pair)) : null);
    }

    private FactItem ResultFor(PcItem item, SearchCard card) => new(card, () =>
    {
        Selected = item;
        _openPage();
    }) { ActionText = "Open", Value = _service.IsOnline(item.Id) ? "online" : _service.SeenWords(item.Id) };

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(OtherPcsService? service) => Cards(service, null).Words;
}
