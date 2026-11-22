namespace OmniControl.App.Features.OtherPcs;

/// What "Can't reach it on this network?" shows: its title, then this PC's live rows and the reminders about the other PC, each under an optional heading.
public sealed record ReachChecks(string Title, string ThisHeading, IReadOnlyList<CheckRow> ThisPc, string OtherHeading, IReadOnlyList<CheckRow> OtherPc)
{
    public static readonly ReachChecks None = new(string.Empty, string.Empty, Array.Empty<CheckRow>(), string.Empty, Array.Empty<CheckRow>());
}

/// THE words and rows of the checklist shown while a paired PC is offline and while the list of PCs open to pairing is empty. Pure.
public static class NetworkChecks
{
    public const string Network = "network", Firewall = "firewall", Tailscale = "tailscale";

    private const string MakePrivate = "Make it Private", SetUp = "Set up", AllowPrivate = "Allow on Private networks";

    /// <param name="isPrivate">This PC's network is Private (false: Public); null = not known.</param>
    /// <param name="firewallOk">OmniControl is allowed through Windows Firewall; null = not read.</param>
    public static ReachChecks ForPairedPc(bool? isPrivate, bool? firewallOk)
    {
        var thisPc = new List<CheckRow>();
        if (isPrivate is { } known) thisPc.Add(Row(Network, known ? "Network is Private" : "Network is Public. Other PCs are only found on Private networks.", known, MakePrivate));
        if (firewallOk is { } allowed) thisPc.Add(Row(Firewall, "OmniControl is allowed through Windows Firewall", allowed, AllowPrivate, pill: true));
        return new ReachChecks("Can't reach it on this network? Check:", "This PC (checked now)", thisPc, "The other PC", new[]
        {
            Reminder("It is on, awake and signed in, with OmniControl running"),
            Reminder("Its network is Private too, and it's on the same Wi-Fi or router"),
            Reminder("Or use Tailscale on both: it works across networks", SetUp),
        });
    }

    /// <param name="isPrivate">This PC's network is Private (false: Public); null = not known.</param>
    public static ReachChecks ForPairing(bool? isPrivate)
    {
        var thisPc = new List<CheckRow>();
        if (isPrivate is { } known) thisPc.Add(Row(Network, known ? "This PC's network is Private" : "This PC's network is Public", known, MakePrivate));
        return new ReachChecks("Not showing? Check:", string.Empty, thisPc, string.Empty, new[]
        {
            Reminder("The other PC has a New PC tab open, on the same Wi-Fi, and its network is Private"),
            Reminder("Or pair \"Over the internet\" with Tailscale", SetUp),
        });
    }

    /// Everything either checklist can say (titles, headings, rows, fixes), for search.
    public static IReadOnlyList<string> AllWords() => new[] { ForPairedPc(true, true), ForPairedPc(false, false), ForPairing(true), ForPairing(false) }
        .SelectMany(r => new[] { r.Title, r.ThisHeading, r.OtherHeading }.Concat(r.ThisPc.Concat(r.OtherPc).SelectMany(c => new[] { c.Words, c.Fix })))
        .Where(w => w.Length > 0).Distinct().ToList();

    private static CheckRow Row(string id, string words, bool ok, string fix, bool pill = false) =>
        new(id, words, ok ? CheckState.Ok : CheckState.Failed, string.Empty, string.Empty, string.Empty, ok ? string.Empty : fix, pill);

    private static CheckRow Reminder(string words, string fix = "") =>
        new(fix.Length > 0 ? Tailscale : string.Empty, words, CheckState.Pending, string.Empty, string.Empty, string.Empty, fix);
}
