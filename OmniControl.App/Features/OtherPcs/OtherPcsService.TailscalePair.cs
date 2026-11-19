using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

// Tailscale for a PC that is already paired: the same checklist as pairing over the internet
// (TailscaleChecks) plus the row for that PC. No new pairing: the pairing key is reused, the PC's Tailscale address is learned from the
// tailnet's peers by name, ours is handed over the current link, and when every row passes the link is redialled over Tailscale.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, IReadOnlyList<TailscaleCheck>> _pairChecks = new();
    private readonly HashSet<string> _pairChecking = new();
    private readonly Dictionary<string, (DateTime At, string Address)> _switchFailedAt = new();
    private readonly Dictionary<string, string> _switchProblems = new();
    private readonly Dictionary<string, DateTime> _pairCheckedAt = new();

    /// True while the PC's link really runs over Tailscale (what the "via Tailscale" tag says).
    public bool IsViaTailscale(string id) => _links.TryGetValue(id, out var link) && link.ViaTailscale;

    /// True when the PC is set to Tailscale and Tailscale does not carry its link (yet, or any more): its detail shows the checklist.
    public bool NeedsTailscaleSetup(string id) => Find(id) is { Route: PcRoute.Tailscale } && !RouteConnected(id);

    // The link itself (not the probe) still has to be moved onto Tailscale: what the periodic checks work on, so a passing probe on a LAN link still switches it.
    private bool LinkNeedsTailscale(string id) => Find(id) is { Route: PcRoute.Tailscale } && !IsViaTailscale(id);

    /// The checklist of one paired PC as last read (empty until the first read).
    public IReadOnlyList<TailscaleCheck> PairChecklist(string id) => _pairChecks.GetValueOrDefault(id) ?? Array.Empty<TailscaleCheck>();

    /// True while the checks of that PC are being read.
    public bool PairCheckBusy(string id) => _pairChecking.Contains(id);

    /// Reads Tailscale, the firewall rule, the PC among the tailnet's peers and whether OmniControl answers there; the result arrives through <see cref="Changed"/>, and when every row passes the PC is switched to Tailscale.
    public void CheckPairTailscale(string id)
    {
        if (_disposed || Find(id) is not { } pair || !_pairChecking.Add(id)) return;
        _pairCheckedAt[id] = DateTime.UtcNow;
        var (name, known) = (pair.Name, pair.TailscaleAddress);
        RaiseChanged();
        PcWork.OffUi("Other PCs: Tailscale checks of a PC", async () =>
        {
            TailscaleStatus status = TailscaleStatus.NotInstalled;
            bool? firewall = null, reach = null;
            ProbeResult? probe = null;
            TailscalePeer? peer = null;
            try
            {
                status = TailscaleStatusReader.Read();
                firewall = status.Installed ? TailscaleFirewall.IsAllowed() : null;
                peer = status.Peers.FirstOrDefault(p => known.Length > 0 && p.Address == known) ?? status.Peers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (firewall == true && status.Connected && peer is not null)
                {
                    probe = await ProbeAddressAsync(id, peer.Address).ConfigureAwait(false);
                    reach = probe.Ok;
                }
            }
            finally
            {
                var (read, fw, found, answered, measured) = (status, firewall, peer, reach, probe);
                PcWork.Post(() => FinishPairCheck(id, read, fw, found, answered, measured));
            }
        });
    }

    private void FinishPairCheck(string id, TailscaleStatus status, bool? firewall, TailscalePeer? peer, bool? reach, ProbeResult? probe)
    {
        _pairChecking.Remove(id);
        if (Find(id) is not { } pair)
        {
            RaiseChanged();
            return;
        }
        Tailscale.Took(status);
        var checks = TailscaleChecks.Build(status, firewall, reach, pair.Name, pair.Name, peer is not null).ToList();
        if (probe is not null && peer is not null)
        {
            // The last row is the probe itself: its measured time when it passed, its real reason when it did not.
            if (pair.Route == PcRoute.Tailscale) RecordProbe(id, PcRoute.Tailscale, peer.Address, probe);
            var at = checks.FindIndex(c => c.Id == TailscaleChecks.Reachable);
            if (at >= 0) checks[at] = probe.Ok ? checks[at] with { Tag = $"{probe.Ms} ms", TagTip = "Round trip of an authenticated ping over Tailscale." } : checks[at] with { Sentence = probe.Reason };
        }
        _pairChecks[id] = checks;
        if (pair.Route == PcRoute.Tailscale && peer is not null && TailscaleChecks.AllOk(checks)) TailscaleWorks(id, peer.Address);
        RaiseChanged();
    }

    /// Runs the fix of one row of a paired PC's checklist, then reads that PC's checks again.
    public void RunPairTailscaleFix(string id, string checkId) => Tailscale.RunFix(checkId, () => CheckPairTailscale(id));

    // Every row passed: keep the PC's Tailscale address, hand ours over the current link, then open a Tailscale link BESIDE the working one.
    // The LAN link is only replaced (in Attach) once the Tailscale link is open and authenticated; if it fails the LAN link stays, the stage
    // names the failed step and no new try is made before Timing.PcTailscaleSwitchBackoff.
    private void TailscaleWorks(string id, string address)
    {
        if (Find(id) is not { } pair || IsViaTailscale(id) || _dialing.Contains(id)) return;
        // Only a failure at this very address holds the try back: a failed dial to an old stored address says nothing about the address Tailscale lists now.
        if (_switchFailedAt.TryGetValue(id, out var failed) && failed.Address == address && DateTime.UtcNow - failed.At < Timing.PcTailscaleSwitchBackoff) return;
        pair.TailscaleAddress = address;
        _tailscaleFailed.Remove(id);
        _switchProblems.Remove(id);
        Save();
        var own = Tailscale.OwnAddress;
        if (_links.TryGetValue(id, out var link) && own.Length > 0)
            _ = link.SendAsync(new PcMsg { T = PcMsgType.TailscaleAddress, Text = own });
        Dial(pair, address);
    }

    // The Tailscale link could not be opened although Tailscale and the firewall are fine: LAN is kept, the stage says which step failed.
    private void TailscaleDialFailed(string id, string address)
    {
        _tailscaleFailed.Add(id);
        _switchFailedAt[id] = (DateTime.UtcNow, address);
        _switchProblems[id] = $"OmniControl could not open a link to {address}. The other PC may not have this PC paired, or may be set to LAN. Still using LAN; it tries again later.";
        RaiseChanged();
    }

    /// Why the switch to Tailscale did not happen although the checks pass (empty when it did not fail).
    public string PairSwitchProblem(string id) => _switchProblems.GetValueOrDefault(id) ?? string.Empty;

    // The other PC reached Tailscale and told us its address over the current link: the address is kept for when the owner picks Tailscale here,
    // but it never changes the route chosen on this PC (a PC set to LAN must not move to Tailscale by itself).
    // Only an address of Tailscale's own ranges is taken.
    private void HandleTailscaleAddress(string id, string? text)
    {
        if (Find(id) is not { } pair || text is null || !TailscaleStatusReader.IsTailscaleAddress(text)) return;
        if (pair.TailscaleAddress == text) return;
        pair.TailscaleAddress = text;
        Save();
        if (pair.Route == PcRoute.Tailscale)
        {
            _tailscaleFailed.Remove(id);
            CheckPairTailscale(id);
        }
        RaiseChanged();
    }

    // Called with every link check: a PC set to Tailscale that Tailscale does not carry gets the checks again now and then (this is what
    // switches it over once Tailscale works, and what brings the checklist back if it stops); other PCs lose their stale results.
    private void RecheckTailscalePairs()
    {
        var waiting = _settings.Pairs.Where(p => LinkNeedsTailscale(p.Id) || NeedsTailscaleSetup(p.Id)).Select(p => p.Id).ToList();
        var stale = _pairChecks.Keys.Except(waiting).ToList();
        foreach (var id in stale)
        {
            _pairChecks.Remove(id);
            _switchProblems.Remove(id);
            _switchFailedAt.Remove(id);
        }
        if (stale.Count > 0) RaiseChanged();
        foreach (var id in waiting)
            if (!_pairChecking.Contains(id) && DateTime.UtcNow - _pairCheckedAt.GetValueOrDefault(id) >= Timing.PcTailscaleRecheck) CheckPairTailscale(id);
    }
}
