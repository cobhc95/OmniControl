using System.Diagnostics;
using System.Net.Sockets;

namespace OmniControl.App.Features.OtherPcs;

/// What one probe found: whether the PC answered over that route, the measured round trip and, when it did not, one plain sentence why.
internal sealed record ProbeResult(bool Ok, int Ms, string Reason);

/// The last probe of a paired PC: over which route and address, when, and what it found.
internal sealed record RouteProbe(PcRoute Route, string Address, DateTime At, ProbeResult Result);

// The ONE way a route counts as connected: an authenticated, encrypted ping answered by the other PC with a pong that repeats a fresh nonce,
// sent over its own short connection to the address of that route (its LAN address, its typed address or its Tailscale address). Only the
// holder of the pairing key can open that connection (the session proof) and seal the pong, so an open port or an old result cannot fake it.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, RouteProbe> _probes = new();
    private readonly HashSet<string> _probing = new();

    /// The last probe of a PC, or null before the first one.
    internal RouteProbe? ProbeOf(string id) => _probes.GetValueOrDefault(id);

    /// True only while a probe over the PC's current route succeeded recently.
    public bool RouteConnected(string id) => Find(id) is { } pair && _probes.TryGetValue(id, out var probe)
        && probe.Route == pair.Route && probe.Result.Ok && DateTime.UtcNow - probe.At < Timing.PcProbeFresh;

    /// "Check now": forgets the last probe of a PC and measures its chosen route again at once, also while it rests (the person asked).
    public void CheckRouteNow(string id)
    {
        if (_disposed || Find(id) is null) return;
        _probes.Remove(id);
        StartProbes(id);
        RaiseChanged();
    }

    /// The address a probe of the PC's current route goes to; empty when none is known (a LAN PC not heard on the network lately).
    private string ProbeAddress(PairedPc pair) => pair.Route switch
    {
        PcRoute.Tailscale => pair.TailscaleAddress,
        PcRoute.Address => pair.Address,
        _ => _seen.TryGetValue(pair.Id, out var seen) && DateTime.UtcNow - seen.At < Timing.PcBeaconEvery * 3 ? $"{seen.Address}:{seen.Beacon.Port}" : string.Empty,
    };

    private void RecordProbe(string id, PcRoute route, string address, ProbeResult result)
    {
        _probes[id] = new RouteProbe(route, address, DateTime.UtcNow, result);
        if (result.Ok && Find(id) is { } pair) Wake.HeardAgain(pair); // it answered: awake, so a resting PC is dialled again
        if (route != PcRoute.Tailscale) return;
        if (result.Ok) _tailscaleFailed.Remove(id);
        else _tailscaleFailed.Add(id); // Tailscale does not answer: the PC is found on the network meanwhile, as before
    }

    // Runs with every link check: each PC whose last probe is older than PcProbeEvery (or was made over another route) is probed again;
    // a resting PC only when the person asked (<paramref name="asked"/>).
    private void StartProbes(string? asked = null)
    {
        foreach (var pair in _settings.Pairs.ToList())
        {
            if (Resting(pair) && pair.Id != asked) continue;
            var address = ProbeAddress(pair);
            var due = !_probes.TryGetValue(pair.Id, out var last) || last.Route != pair.Route || DateTime.UtcNow - last.At >= Timing.PcProbeEvery;
            if (!due || _probing.Contains(pair.Id) || !_secrets.ContainsKey(pair.Id)) continue;
            if (address.Length == 0)
            {
                _probes[pair.Id] = new RouteProbe(pair.Route, address, DateTime.UtcNow, new ProbeResult(false, 0, "It is not seen on this network."));
                continue;
            }
            _probing.Add(pair.Id);
            var (id, route) = (pair.Id, pair.Route);
            PcWork.OffUi("Other PCs: probing a route", async () =>
            {
                var result = await ProbeAddressAsync(id, address).ConfigureAwait(false);
                PcWork.Post(() =>
                {
                    _probing.Remove(id);
                    if (Find(id) is not { } now || now.Route != route) return; // the route was changed meanwhile: this answer is for the old one
                    RecordProbe(id, route, address, result);
                    RaiseChanged();
                });
            });
        }
    }

    /// Opens a probe connection to <paramref name="address"/>, proves both ends hold the pairing key, sends a sealed ping with a fresh nonce and waits for the pong that repeats it. Never throws for a network problem: the reason is the result.
    private async Task<ProbeResult> ProbeAddressAsync(string peerId, string address)
    {
        if (!PcProtocol.TryParseAddress(address, out var host, out var port)) return new ProbeResult(false, 0, "No address is known for this route.");
        if (_secrets.GetValueOrDefault(peerId) is not { } secret) return new ProbeResult(false, 0, "Its pairing key is missing on this PC.");
        using var client = new TcpClient();
        try
        {
            using (var connect = PcWire.Step(_stop.Token, Timing.PcProbeTimeout)) await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
            client.NoDelay = true;
            using var stream = new NetworkStream(client.Client, ownsSocket: false);
            using var step = PcWire.Step(_stop.Token, Timing.PcProbeTimeout);
            using var link = await PcSession.DialAsync(stream, OwnId, peerId, secret, step.Token, PcProtocol.WantsProbe).ConfigureAwait(false);
            if (link is null) return new ProbeResult(false, 0, "Authentication failed: that PC did not prove it holds the pairing key. Unpair it on both PCs and pair again.");
            var nonce = Convert.ToBase64String(PcCrypto.Random(16));
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            link.Received += message =>
            {
                if (message.T == PcMsgType.Pong && message.Text == nonce) answered.TrySetResult(true);
            };
            _ = Guard.RunAsync("Other PCs: probe link", link.RunAsync);
            var clock = Stopwatch.StartNew();
            if (!await link.SendAsync(new PcMsg { T = PcMsgType.Ping, Text = nonce }).ConfigureAwait(false)) return new ProbeResult(false, 0, "The connection closed before it answered.");
            await answered.Task.WaitAsync(Timing.PcProbeTimeout, _stop.Token).ConfigureAwait(false);
            return new ProbeResult(true, (int)clock.ElapsedMilliseconds, string.Empty);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return new ProbeResult(false, 0, "Timed out: nothing answered in time.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new ProbeResult(false, 0, "Refused: OmniControl is not listening there.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            return new ProbeResult(false, 0, "It could not be reached there.");
        }
    }

    // The listening side of a probe: the same proof as a session (an unknown PC or a wrong proof gets no answer), then every sealed ping is answered
    // with a pong that repeats its nonce; the dialling PC ends the connection, and a silent one is cut after PcProbeTimeout.
    private async Task AnswerProbeAsync(Stream stream)
    {
        using var link = await PcSession.AcceptAsync(stream, OwnId, id => _secrets.GetValueOrDefault(id), _stop.Token).ConfigureAwait(false);
        if (link is null) return;
        link.Received += message =>
        {
            if (message.T == PcMsgType.Ping && message.Text is { Length: > 0 and <= 64 } nonce) _ = link.SendAsync(new PcMsg { T = PcMsgType.Pong, Text = nonce });
        };
        await Task.WhenAny(Guard.RunAsync("Other PCs: probe answer", link.RunAsync), Task.Delay(Timing.PcProbeTimeout, _stop.Token)).ConfigureAwait(false);
    }
}
