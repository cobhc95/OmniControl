using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// THE checklist of "Over the internet": the order, the words, the sentence and the fix of each check, made from what was read. Pure.
public static class TailscaleChecks
{
    public const string Installed = "installed", Running = "running", SignedIn = "signedin", Connected = "connected", Address = "address", Firewall = "firewall", Peer = "peer", Reachable = "reachable";

    /// Added to the reach row's sentence when it is the only row that fails.
    public const string ReachHint = "That PC may need Tailscale restarted: open OmniControl there.";

    /// <param name="firewallOk">The OmniControl rule is there; null = not read.</param>
    /// <param name="reachable">The other PC answered; null = no PC picked or not tried yet.</param>
    /// <param name="target">The other PC as the person gave it (for the sentence).</param>
    /// <param name="peerName">Set for a PC that is already paired: the list gets the extra row "&lt;PC&gt; on your Tailscale", before the reach row.</param>
    /// <param name="peerFound">That PC is among the tailnet's peers.</param>
    public static IReadOnlyList<CheckRow> Build(TailscaleStatus status, bool? firewallOk, bool? reachable, string target, string? peerName = null, bool peerFound = false)
    {
        var checks = new List<CheckRow>();
        var blocked = false; // once one fails, the ones after it cannot be known

        void Add(string id, string words, bool? ok, string tag, string tagTip, string sentence, string fix)
        {
            var state = blocked || ok is null ? CheckState.Pending : ok.Value ? CheckState.Ok : CheckState.Failed;
            checks.Add(new CheckRow(id, words, state, state == CheckState.Ok ? tag : string.Empty, state == CheckState.Ok ? tagTip : string.Empty, state == CheckState.Failed ? sentence : string.Empty, state == CheckState.Failed ? fix : string.Empty, id is Firewall or Address));
            if (state != CheckState.Ok) blocked = true;
        }

        Add(Installed, "Installed", status.Installed, status.Version, string.Empty, "Tailscale is not installed on this PC.", "Get Tailscale");
        Add(Running, "Running", status.Running, string.Empty, string.Empty, "Tailscale is not running.", "Start Tailscale");
        Add(SignedIn, "Signed in", status.SignedIn, string.Empty, status.Account, "Tailscale is not signed in.", "Sign in");
        Add(Connected, "Connected", status.Connected, status.OwnAddress, string.Empty, "Tailscale is not connected to your network yet.", string.Empty);
        Add(Address, "Tailscale has its address on this PC", status.AddressOnAdapter, string.Empty, string.Empty, "Windows lost Tailscale's address on this PC, so other PCs can't reach OmniControl.", "Restart Tailscale");
        Add(Firewall, "OmniControl allowed through Windows Firewall on Tailscale", firewallOk, string.Empty, string.Empty, "Windows Firewall blocks other PCs from reaching OmniControl on Tailscale.", "Allow");
        if (peerName is not null)
            Add(Peer, $"{peerName} on your Tailscale", peerFound, string.Empty, string.Empty, $"{peerName} is not on your Tailscale network. Install Tailscale on it and sign in with the same account.", "How to add it");
        var hint = peerName is not null && !blocked ? $" {ReachHint}" : string.Empty;
        Add(Reachable, peerName is null ? "Other PC reachable" : "Reaches OmniControl on it", reachable, string.Empty, string.Empty, $"{(target.Length > 0 ? target : "The other PC")} can't be reached. Check that it is on, signed in to Tailscale and has OmniControl open.{hint}", string.Empty);
        return checks;
    }

    /// The first check that fails: the one thing to fix first (null when none fails). True when every check passes.
    public static bool AllOk(IEnumerable<CheckRow> checks) => checks.All(c => c.State == CheckState.Ok);
}
