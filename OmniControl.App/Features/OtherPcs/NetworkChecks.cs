namespace OmniControl.App.Features.OtherPcs;

/// What "Can't reach it on this network?" shows: its title, then this PC's live rows and the reminders about the other PC, each under an optional heading.
public sealed record ReachChecks(string Title, string ThisHeading, IReadOnlyList<CheckRow> ThisPc, string OtherHeading, IReadOnlyList<CheckRow> OtherPc)
{
    public static readonly ReachChecks None = new(string.Empty, string.Empty, Array.Empty<CheckRow>(), string.Empty, Array.Empty<CheckRow>());
}

/// This PC's network as other PCs meet it: not read yet, Windows' list could not be read, no network, Public, Private, or Windows gave no category.
public enum NetworkReach { NotRead, Failed, None, Public, Private, Unknown }

/// What Windows Firewall does with OmniControl's ports: not read yet, allowed, no allow rule, a block rule, or it could not be read.
public enum FirewallReach { NotRead, Allowed, NoRule, Blocked, Failed }

/// Everything this PC knows that can stop discovery: its network, its firewall, whether it holds its ports, the name of a PC heard on another
/// OmniControl version (empty: none) and, for a paired PC, that its pairing key could not be read here.
public sealed record ReachFacts(NetworkReach Network, FirewallReach Firewall, bool PortsHeld, string OtherVersion, bool KeyMissing = false);

/// THE words and rows of the checklist shown while a paired PC is offline and while the list of PCs open to pairing is empty. Pure.
public static class NetworkChecks
{
    public const string Network = "network", Firewall = "firewall", Tailscale = "tailscale";

    private const string MakePrivate = "Make it Private", OpenSettings = "Open network settings", SetUp = "Set up", AllowPrivate = "Allow on Private networks";

    public static ReachChecks ForPairedPc(ReachFacts facts, string logPath) =>
        new("Can't reach it on this network? Check:", "This PC (checked now)", ThisPc(facts, logPath, "Network"), "The other PC", new[]
        {
            Reminder("It is on, awake and signed in, running the same OmniControl version"),
            Reminder("Its network is Private too, on the same Wi-Fi or router (not a guest network)"),
            Reminder("Or use Tailscale on both: it works across networks", SetUp),
        });

    public static ReachChecks ForPairing(ReachFacts facts, string logPath) =>
        new("Not showing? Check:", string.Empty, ThisPc(facts with { KeyMissing = false }, logPath, "This PC's network"), string.Empty, new[]
        {
            Reminder("The other PC has a New PC tab open, on the same Wi-Fi (not a guest network), and its network is Private"),
            Reminder("Or pair \"Over the internet\" with Tailscale", SetUp),
        });

    // Most likely first; a fact not read yet is left out (it is being read), one that could not be read says so instead of hiding.
    private static List<CheckRow> ThisPc(ReachFacts facts, string logPath, string network)
    {
        var rows = new List<CheckRow>();
        var notChecked = $"The details are in {logPath}.";
        switch (facts.Network)
        {
            case NetworkReach.None: rows.Add(Failed(Network, "This PC isn't connected to a network", string.Empty, OpenSettings)); break;
            case NetworkReach.Public: rows.Add(Failed(Network, $"{network} is Public. Other PCs are only found on Private networks.", string.Empty, MakePrivate)); break;
            case NetworkReach.Private: rows.Add(Ok($"{network} is Private")); break;
            case NetworkReach.Unknown: rows.Add(Unsure(Network, "Windows doesn't say whether this network is Private", "Other PCs are only found on Private networks.", OpenSettings)); break;
            case NetworkReach.Failed: rows.Add(Unsure(string.Empty, "Couldn't check this PC's network; see the log", notChecked)); break;
        }
        switch (facts.Firewall)
        {
            case FirewallReach.Allowed: rows.Add(Ok("OmniControl is allowed through Windows Firewall")); break;
            case FirewallReach.NoRule: rows.Add(Failed(Firewall, "OmniControl is allowed through Windows Firewall", string.Empty, AllowPrivate, pill: true)); break;
            case FirewallReach.Blocked:
                rows.Add(Failed(string.Empty, "Windows Firewall blocks OmniControl", "Remove OmniControl's block rule in Windows Security: Firewall & network protection, Advanced settings, Inbound Rules.", string.Empty));
                break;
            case FirewallReach.Failed: rows.Add(Unsure(string.Empty, "Couldn't check Windows Firewall; see the log", notChecked)); break;
        }
        if (!facts.PortsHeld)
            rows.Add(Failed(string.Empty, $"Another program uses OmniControl's ports ({PcProtocol.TcpPort}, {PcProtocol.UdpPort})", "OmniControl tries again by itself; close that program or restart this PC.", string.Empty));
        if (facts.OtherVersion.Length > 0)
            rows.Add(Failed(string.Empty, $"{facts.OtherVersion} runs a different OmniControl version", "PCs only find each other on the same version: update both.", string.Empty));
        if (facts.KeyMissing)
            rows.Add(Failed(string.Empty, "This PC can't read its pairing key for it", "Windows' credential store lost it: forget this PC and pair the two again.", string.Empty));
        return rows;
    }

    /// Everything either checklist can say (titles, headings, rows, fixes), for search.
    public static IReadOnlyList<string> AllWords()
    {
        var all = Enum.GetValues<NetworkReach>().Zip(Enum.GetValues<FirewallReach>().Concat(new[] { FirewallReach.NoRule }),
            (network, firewall) => new ReachFacts(network, firewall, false, "A PC", true)).ToList();
        return all.SelectMany(f => new[] { ForPairedPc(f, string.Empty), ForPairing(f, string.Empty) })
            .SelectMany(r => new[] { r.Title, r.ThisHeading, r.OtherHeading }.Concat(r.ThisPc.Concat(r.OtherPc).SelectMany(c => new[] { c.Words, c.Fix })))
            .Where(w => w.Length > 0).Distinct().ToList();
    }

    private static CheckRow Ok(string words) => new(string.Empty, words, CheckState.Ok, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CheckRow Failed(string id, string words, string sentence, string fix, bool pill = false) =>
        new(id, words, CheckState.Failed, string.Empty, string.Empty, sentence, fix, pill);

    // Not known: the plain reminder mark, the reason in the tooltip.
    private static CheckRow Unsure(string id, string words, string tip, string fix = "") =>
        new(id, words, CheckState.Pending, string.Empty, tip, string.Empty, fix);

    private static CheckRow Reminder(string words, string fix = "") =>
        new(fix.Length > 0 ? Tailscale : string.Empty, words, CheckState.Pending, string.Empty, string.Empty, string.Empty, fix);
}
