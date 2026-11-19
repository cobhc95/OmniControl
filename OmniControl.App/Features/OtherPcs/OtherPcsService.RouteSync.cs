namespace OmniControl.App.Features.OtherPcs;

// The route (LAN / Tailscale / other address) of a pair is changed on BOTH PCs at once, so a one-sided change can never make them lose each other.
// Picking a route sends a "route" message over the CURRENT working link first, then switches here; the other PC saves the same route and logs the
// cause as "changed on <name>". When there is no link the change is kept pending (PairedPc.PendingRoute) and sent on the next contact. A connection
// from a paired PC is always accepted (Attach), and one it opened over Tailscale makes this PC adopt Tailscale as well.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, string> _routeNotes = new();

    /// One honest sentence about the last route change of a PC ("Switched on both PCs", "X will switch when it next connects"); empty when there is none.
    public string RouteNote(string id) => _routeNotes.GetValueOrDefault(id) ?? string.Empty;

    // Tells the PC behind the link which route this PC now uses; true when the message was written.
    private static Task<bool> SendRoute(PcLink link, PcRoute route) => link.SendAsync(new PcMsg { T = PcMsgType.Route, Text = route.ToString() });

    // A pending change goes out as soon as a link to that PC is up.
    private void SendPendingRoute(PcLink link, PairedPc pair)
    {
        if (pair.PendingRoute is not { } route) return;
        var id = pair.Id;
        _ = Guard.RunAsync("Other PCs: route change", async () =>
        {
            if (!await SendRoute(link, route).ConfigureAwait(false)) return;
            PcWork.Post(() =>
            {
                if (Find(id) is not { } now || now.PendingRoute != route) return;
                now.PendingRoute = null;
                Save();
                _routeNotes[id] = "Switched on both PCs.";
                RaiseChanged();
            });
        });
    }

    // The other PC chose a route and told us over the working link: take the same one (an address route needs an address of its own here).
    private void HandleRoute(string id, string? text)
    {
        if (Find(id) is not { } pair || !Enum.TryParse<PcRoute>(text, out var route) || !Enum.IsDefined(route)) return;
        pair.PendingRoute = null; // the newest choice wins; a change of ours still waiting would only undo it
        if (pair.Route == route) return;
        if (route == PcRoute.Address && pair.Address.Length == 0)
        {
            _routeNotes[id] = $"{pair.DisplayName} chose another address; type its address here to follow.";
            return;
        }
        SwitchRoute(pair, route, $"changed on {pair.DisplayName}");
        _routeNotes[id] = $"Switched on both PCs (changed on {pair.DisplayName}).";
        AfterRouteChange(id, route);
        RaiseChanged();
    }

    // The ONE place a pair's route is written (the log names the cause).
    private void SwitchRoute(PairedPc pair, PcRoute route, string cause)
    {
        LogRoute(pair, route, cause);
        pair.Route = route;
        _probes.Remove(pair.Id); // what was measured over the old route says nothing about the new one
        Save();
    }

    private void AfterRouteChange(string id, PcRoute route)
    {
        if (route == PcRoute.Tailscale) CheckPairTailscale(id);
        else
        {
            CloseLink(id); // dialled again by the new route
            CheckLinksSoon();
        }
    }
}
