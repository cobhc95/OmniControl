using System.Net;
using System.Net.Sockets;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.OtherPcs;

// The network side: the listener, the beacons, dialling paired PCs and keeping the links alive.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, PcLink> _links = new();
    private readonly Dictionary<string, Seen> _seen = new();
    private readonly HashSet<string> _dialing = new();
    private readonly HashSet<string> _tailscaleFailed = new(); // PCs set to Tailscale whose Tailscale address did not answer: found on the network until Tailscale works again
    private TcpListener? _listener;
    private PcDiscovery? _discovery;
    private bool _started;
    private int _handshakes;

    /// <summary>A beacon heard from another PC: where it was, and when.</summary>
    private sealed record Seen(PcBeacon Beacon, string Address, DateTime At);

    /// <summary>Starts listening and announcing (once); nothing happens before a PC is paired or pairing is opened.</summary>
    private void StartNetwork()
    {
        if (_started || _disposed || StartupMode.Inert) return;
        _started = true;
        StartListener();
        _discovery = new PcDiscovery(OwnBeacon, (beacon, from) => Post(() => Heard(beacon, from.ToString())));
        if (!_discovery.Start(out var problem))
        {
            _discovery.Dispose();
            _discovery = null;
            Guard.Log("Other PCs: discovery", new IOException(problem));
            _report("Other PCs can't look for PCs on the network now (the port is in use). A PC can still be paired or connected by its address.");
        }
        _ = Guard.RunAsync("Other PCs: link checks", CheckLinksAsync);
    }

    private void StopNetwork()
    {
        _listener?.Stop();
        _discovery?.Dispose();
    }

    private PcBeacon OwnBeacon() => new(PcProtocol.BeaconTag, OwnId, _ownName, PcProtocol.TcpPort, PairingIsOpen);

    private void StartListener()
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, PcProtocol.TcpPort);
            listener.Start();
            _listener = listener;
        }
        catch (SocketException ex)
        {
            Guard.Log("Other PCs: listening", ex);
            _report("Other PCs can't accept connections now (the port is in use). This PC can still connect to the others.");
            return;
        }
        _ = Guard.RunAsync("Other PCs: accepting connections", () => AcceptAsync(_listener));
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = Guard.RunAsync("Other PCs: incoming connection", () => HandleIncomingAsync(client));
            }
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            // Stopped, or the listener was closed: nothing more to accept.
        }
    }

    // A connection says what it wants with its first byte. Before it proved who it is (a pairing's code, a paired PC's proof) it gets
    // at most a few seconds, a few bytes and one of a few places, and an unknown PC gets no answer at all.
    private async Task HandleIncomingAsync(TcpClient client)
    {
        PcLink? link = null;
        var counted = Interlocked.Increment(ref _handshakes) <= Timing.PcHandshakesMax;
        try
        {
            if (!counted) return;
            client.NoDelay = true;
            var viaTailscale = TailscaleStatusReader.IsTailscaleAddress((client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString());
            var stream = new NetworkStream(client.Client, ownsSocket: true);
            using var first = PcWire.Step(_stop.Token);
            if (await PcWire.ReadExactAsync(stream, 1, first.Token).ConfigureAwait(false) is not { } kind) return;
            if (kind[0] == PcProtocol.WantsPairing) await RespondToPairingAsync(stream).ConfigureAwait(false);
            else if (kind[0] == PcProtocol.WantsProbe) await AnswerProbeAsync(stream).ConfigureAwait(false);
            else if (kind[0] == PcProtocol.WantsSession)
            {
                link = await PcSession.AcceptAsync(stream, OwnId, id => _secrets.GetValueOrDefault(id), _stop.Token).ConfigureAwait(false);
                if (link is not null) link.ViaTailscale = viaTailscale;
            }
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            // The other end left or was too slow: the connection is dropped below.
        }
        finally
        {
            Interlocked.Decrement(ref _handshakes);
            if (link is null) client.Dispose();
        }
        if (link is not null) Post(() => Attach(link));
    }

    // ---- Who is nearby ----

    /// <summary>The PCs on the network that are open to pairing now and are not paired yet.</summary>
    public IReadOnlyList<NearbyPc> Nearby => _seen.Values
        .Where(s => s.Beacon.Pairing && DateTime.UtcNow - s.At < Timing.PcBeaconEvery * 3 && Find(s.Beacon.Id) is null)
        .Select(s => new NearbyPc(s.Beacon.Id, s.Beacon.Name, s.Address, s.Beacon.Port)).OrderBy(n => n.Name).ToList();

    private void Heard(PcBeacon beacon, string address)
    {
        if (_disposed || beacon.Id == OwnId) return;
        var before = Nearby.Count;
        _seen[beacon.Id] = new Seen(beacon, address, DateTime.UtcNow);
        if (Nearby.Count != before) RaiseChanged();
        if (Find(beacon.Id) is { Route: not PcRoute.Address } pair && !_links.ContainsKey(pair.Id)) DialIfMine(pair);
    }

    // ---- Links ----

    // The PC with the smaller id dials PCs it finds on the network, so two PCs that see each other do not dial each other at once;
    // a PC reached by an address (typed for it, or its Tailscale address) is dialled from this side whatever the ids. A PC set to Tailscale
    // that Tailscale has not reached (yet, or any more) is found on the network meanwhile, as a PC on LAN is.
    private void DialIfMine(PairedPc pair)
    {
        if (!_secrets.ContainsKey(pair.Id) || _dialing.Contains(pair.Id)) return;
        if (pair.Route == PcRoute.Address)
        {
            if (pair.Address.Length > 0) Dial(pair, pair.Address);
        }
        else if (pair.Route == PcRoute.Tailscale && pair.TailscaleAddress.Length > 0 && !_tailscaleFailed.Contains(pair.Id)) Dial(pair, pair.TailscaleAddress);
        else if (string.CompareOrdinal(OwnId, pair.Id) < 0 && _seen.TryGetValue(pair.Id, out var seen) && DateTime.UtcNow - seen.At < Timing.PcBeaconEvery * 3)
            Dial(pair, $"{seen.Address}:{seen.Beacon.Port}");
    }

    private void CheckLinksSoon() => Post(CheckLinks);

    private async Task CheckLinksAsync()
    {
        using var timer = new PeriodicTimer(Timing.PcLinkCheck);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false)) Post(CheckLinks);
        }
        catch (OperationCanceledException)
        {
            // Stopped: the normal end.
        }
    }

    private void CheckLinks()
    {
        if (_disposed) return;
        foreach (var link in _links.Values.ToList())
        {
            if (DateTime.UtcNow - link.LastReceived > Timing.PcLinkSilence) link.Close();
            else _ = link.SendAsync(new PcMsg { T = PcMsgType.Ping });
        }
        foreach (var pair in _settings.Pairs.Where(p => !_links.ContainsKey(p.Id)).ToList()) DialIfMine(pair);
        RecheckTailscalePairs();
        StartProbes();
        var nearbyNow = Nearby.Count;
        if (nearbyNow != _nearbyShown)
        {
            _nearbyShown = nearbyNow;
            RaiseChanged();
        }
    }

    private int _nearbyShown;

    private void Dial(PairedPc pair, string address)
    {
        if (!TryParseAddress(address, out var host, out var port) || _secrets.GetValueOrDefault(pair.Id) is not { } secret) return;
        _dialing.Add(pair.Id);
        var viaTailscale = pair.Route == PcRoute.Tailscale && address == pair.TailscaleAddress;
        _ = Guard.RunAsync("Other PCs: dialling", async () =>
        {
            PcLink? link = null;
            try
            {
                link = await DialAsync(pair.Id, host, port, secret).ConfigureAwait(false);
            }
            finally
            {
                var made = link;
                Post(() =>
                {
                    _dialing.Remove(pair.Id);
                    if (made is not null) Attach(made);
                    else if (viaTailscale) TailscaleDialFailed(pair.Id, address);
                });
            }
        });
    }

    private async Task<PcLink?> DialAsync(string peerId, string host, int port, byte[] secret)
    {
        var client = new TcpClient();
        try
        {
            using (var connect = PcWire.Step(_stop.Token)) await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
            client.NoDelay = true;
            var stream = new NetworkStream(client.Client, ownsSocket: true);
            var viaTailscale = TailscaleStatusReader.IsTailscaleAddress((client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString());
            var link = await PcSession.DialAsync(stream, OwnId, peerId, secret, _stop.Token).ConfigureAwait(false);
            if (link is null) client.Dispose();
            else link.ViaTailscale = viaTailscale;
            return link;
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            client.Dispose();
            return null; // the PC is not there (now): the next check tries again
        }
    }

    /// <summary>"host" or "host:port" (the default port when none): false when it is not one.</summary>
    internal static bool TryParseAddress(string text, out string host, out int port)
    {
        host = string.Empty;
        port = PcProtocol.TcpPort;
        text = text.Trim();
        if (text.Length == 0) return false;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon) // one colon: host:port (an IPv6 address has several and is taken whole)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            text = text[..colon];
        }
        host = text;
        return host.Length > 0;
    }

    // A second link to the same PC (both dialled, or it restarted): the one the PC with the smaller id dialled stays; two the same way
    // round mean the other PC restarted, and the newer one stays.
    private void Attach(PcLink link)
    {
        if (_disposed || Find(link.PeerId) is not { } pair)
        {
            link.Dispose();
            return;
        }
        if (link.ViaTailscale && pair.Route != PcRoute.Tailscale)
        {
            // The route chosen here is the one source: the other PC dialling over Tailscale does not override it (it falls back to LAN when it finds no link there).
            link.Dispose();
            return;
        }
        if (_links.TryGetValue(pair.Id, out var old))
        {
            var smallerHere = string.CompareOrdinal(OwnId, pair.Id) < 0;
            if (old.ViaTailscale && !link.ViaTailscale) // a LAN link never replaces a Tailscale one; a Tailscale one replaces the LAN link it was opened beside
            {
                link.Dispose();
                return;
            }
            if (old.ViaTailscale == link.ViaTailscale && old.Dialled != link.Dialled && old.Dialled == smallerHere)
            {
                link.Dispose();
                return;
            }
            old.Dispose();
        }
        _links[pair.Id] = link;
        if (link.ViaTailscale)
        {
            _tailscaleFailed.Remove(pair.Id);
            _switchProblems.Remove(pair.Id);
        }
        _catalogKey.Remove(pair.Id);
        link.Received += message => Post(() => OnMessage(link, message));
        link.Closed += () => Post(() => OnLinkClosed(link));
        _ = Guard.RunAsync("Other PCs: link", link.RunAsync);
        SendCatalog(link, force: true);
        RaiseChanged();
    }

    private void OnLinkClosed(PcLink link)
    {
        if (!_links.TryGetValue(link.PeerId, out var current) || current != link) return; // a newer link replaced it
        _links.Remove(link.PeerId);
        _catalogKey.Remove(link.PeerId);
        SetAvailable(link.PeerId, false);
        if (Find(link.PeerId) is { } seen)
        {
            seen.LastSeen = DateTime.Now;
            Save();
        }
        link.Dispose();
        RaiseChanged();
    }

    private void CloseLink(string id)
    {
        if (_links.TryGetValue(id, out var link)) link.Close();
    }
}
