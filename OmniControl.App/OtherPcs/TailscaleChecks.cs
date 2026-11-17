using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.OtherPcs;

/// <summary>How one check of the Tailscale checklist stands: not known yet (an earlier one failed, or no PC is picked), fine, or failing.</summary>
public enum CheckState { Pending, Ok, Failed }

/// <summary>One line of the checklist: what it asks, how it stands, a short fact for its right side (the version, this PC's address), and the one fix (words empty when there is none).</summary>
public sealed record TailscaleCheck(string Id, string Words, CheckState State, string Tag, string TagTip, string Sentence, string Fix);

/// <summary>THE checklist of "Over the internet" (docs/mockups/other-pcs-tailscale.html): the order, the words, the sentence and the fix of each check, made from what was read. Pure.</summary>
public static class TailscaleChecks
{
    public const string Installed = "installed", Running = "running", SignedIn = "signedin", Connected = "connected", Firewall = "firewall", Peer = "peer", Reachable = "reachable";

    /// <param name="firewallOk">The OmniControl rule is there; null = not read.</param>
    /// <param name="reachable">The other PC answered; null = no PC picked or not tried yet.</param>
    /// <param name="target">The other PC as the person gave it (for the sentence).</param>
    /// <param name="peerName">Set for a PC that is already paired (docs/mockups/other-pcs-tailscale-connect.html): the list gets the extra row "&lt;PC&gt; on your Tailscale", before the reach row.</param>
    /// <param name="peerFound">That PC is among the tailnet's peers.</param>
    public static IReadOnlyList<TailscaleCheck> Build(TailscaleStatus status, bool? firewallOk, bool? reachable, string target, string? peerName = null, bool peerFound = false)
    {
        var checks = new List<TailscaleCheck>();
        var blocked = false; // once one fails, the ones after it cannot be known

        void Add(string id, string words, bool? ok, string tag, string tagTip, string sentence, string fix)
        {
            var state = blocked || ok is null ? CheckState.Pending : ok.Value ? CheckState.Ok : CheckState.Failed;
            checks.Add(new TailscaleCheck(id, words, state, state == CheckState.Ok ? tag : string.Empty, state == CheckState.Ok ? tagTip : string.Empty, state == CheckState.Failed ? sentence : string.Empty, state == CheckState.Failed ? fix : string.Empty));
            if (state != CheckState.Ok) blocked = true;
        }

        Add(Installed, "Installed", status.Installed, status.Version, string.Empty, "Tailscale is not installed on this PC.", "Get Tailscale");
        Add(Running, "Running", status.Running, string.Empty, string.Empty, "Tailscale is not running.", "Start Tailscale");
        Add(SignedIn, "Signed in", status.SignedIn, string.Empty, status.Account, "Tailscale is not signed in.", "Sign in");
        Add(Connected, "Connected", status.Connected, status.OwnAddress, string.Empty, "Tailscale is not connected to your network yet.", string.Empty);
        Add(Firewall, "OmniControl allowed through Windows Firewall on Tailscale", firewallOk, string.Empty, string.Empty, "Windows Firewall blocks other PCs from reaching OmniControl on Tailscale.", "Allow");
        if (peerName is not null)
            Add(Peer, $"{peerName} on your Tailscale", peerFound, string.Empty, string.Empty, $"{peerName} is not on your Tailscale network. Install Tailscale on it and sign in with the same account.", "How to add it");
        Add(Reachable, peerName is null ? "Other PC reachable" : "Reaches OmniControl on it", reachable, string.Empty, string.Empty, $"{(target.Length > 0 ? target : "The other PC")} can't be reached. Check that it is on, signed in to Tailscale and has OmniControl open.", string.Empty);
        return checks;
    }

    /// <summary>The first check that fails: the one thing to fix first (null when none fails).</summary>
    /// <summary>True when every check passes.</summary>
    public static bool AllOk(IEnumerable<TailscaleCheck> checks) => checks.All(c => c.State == CheckState.Ok);

    public static TailscaleCheck? FirstFailing(IEnumerable<TailscaleCheck> checks) => checks.FirstOrDefault(c => c.State == CheckState.Failed);
}
