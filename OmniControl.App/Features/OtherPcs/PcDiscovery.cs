using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace OmniControl.App.Features.OtherPcs;

/// Finding PCs on the local network: this PC sends a small broadcast (<see cref="PcBeacon"/>: id, name, port, whether pairing is open) every
/// <see cref="Timing.PcBeaconEvery"/>, and reports every one it hears from another PC. A beacon is not trusted for anything but "where to
/// dial": whoever answers must still prove the pairing key (<see cref="PcSession"/>) and a pairing needs the typed code.
internal sealed class PcDiscovery : IDisposable
{
    private const int BeaconMax = 512;
    private readonly Func<PcBeacon> _own;
    private readonly Action<IReadOnlyList<(PcBeacon Beacon, IPAddress From)>> _heard;
    private readonly Func<PcNetworkView?> _networks;
    private readonly Action _refreshNetworks;
    private readonly Action<string> _otherVersion;
    private readonly Dictionary<string, (PcBeacon, IPAddress)> _waiting = new(); // heard since the last hand-over (locked), the latest per PC
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _udp;

    /// <param name="own">What this PC says about itself now.</param>
    /// <param name="heard">The beacons heard since the last call and where each came from (on a worker thread, at most one call per
    /// <see cref="Timing.PcBeaconHandOver"/>).</param>
    /// <param name="networks">The networks beacons may go to and come from; null while unknown (then every network, as before).</param>
    /// <param name="refreshNetworks">Reads the networks again (called on the announcing worker now and then).</param>
    /// <param name="otherVersion">The name of a PC heard announcing another protocol version (on the listening worker).</param>
    public PcDiscovery(Func<PcBeacon> own, Action<IReadOnlyList<(PcBeacon Beacon, IPAddress From)>> heard, Func<PcNetworkView?> networks, Action refreshNetworks, Action<string> otherVersion)
    {
        _otherVersion = otherVersion;
        _own = own;
        _heard = heard;
        _networks = networks;
        _refreshNetworks = refreshNetworks;
    }

    /// Opens the socket and starts announcing and listening; false (with the reason) when Windows or another program holds the port.
    public bool Start(out string? problem)
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true); // which adapter each beacon came in on
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, PcProtocol.UdpPort));
            _udp = udp;
        }
        catch (SocketException ex)
        {
            problem = ex.Message;
            return false;
        }
        problem = null;
        _ = Guard.RunAsync("Other PCs: listening for beacons", ListenAsync);
        _ = Guard.RunAsync("Other PCs: announcing", AnnounceAsync);
        return true;
    }

    /// The socket still hears beacons (false once it died with its network: the owner starts a new one).
    public bool Listening => !_deaf;

    private volatile bool _deaf;

    /// Announces now (a pairing was opened or closed).
    public void AnnounceNow() => Send();

    private async Task AnnounceAsync()
    {
        using var timer = new PeriodicTimer(Timing.PcBeaconEvery);
        try
        {
            do
            {
                if (DateTime.UtcNow - _networksAt >= Timing.PcBroadcastRefresh) // a network's category can change without its addresses
                {
                    _networksAt = DateTime.UtcNow;
                    _refreshNetworks();
                }
                Send();
            }
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Stopped: the normal end.
        }
    }

    private PcBeacon? _lastBeacon;
    private byte[] _packet = Array.Empty<byte>();
    private DateTime _networksAt = DateTime.MinValue;

    private readonly object _sendGate = new(); // AnnounceNow (UI thread) and the timer (a worker) both send

    private void Send()
    {
        lock (_sendGate) SendNow();
    }

    private void SendNow()
    {
        if (_udp is not { } udp) return;
        var beacon = _own();
        if (beacon != _lastBeacon) (_lastBeacon, _packet) = (beacon, JsonSerializer.SerializeToUtf8Bytes(beacon, PcProtocol.Json)); // made again only when it changed
        var packet = _packet;
        foreach (var target in _networks()?.Broadcasts() ?? new[] { IPAddress.Broadcast })
        {
            try
            {
                udp.Send(packet, packet.Length, new IPEndPoint(target, PcProtocol.UdpPort));
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // That network is not reachable now (cable out, adapter off); the others still get it.
            }
        }
    }

    private async Task ListenAsync()
    {
        if (_udp is not { } udp) return;
        var second = Environment.TickCount64 / 1000;
        var heardThisSecond = 0;
        var buffer = new byte[BeaconMax + 1];
        EndPoint anyone = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                SocketReceiveMessageFromResult received;
                try
                {
                    received = await udp.Client.ReceiveMessageFromAsync(buffer, SocketFlags.None, anyone, _stop.Token).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue; // Windows ends a UDP receive this way when an earlier send met "port unreachable"; listening goes on
                }
                var now = Environment.TickCount64 / 1000;
                if (now != second) (second, heardThisSecond) = (now, 0);
                if (++heardThisSecond > Timing.PcBeaconsPerSecond) continue; // a flood of beacons is dropped, not handed on
                if (received.RemoteEndPoint is not IPEndPoint from || (_networks() is { } networks && !networks.HearsFrom(received.PacketInformation.Interface, from.Address)))
                    continue; // a Public network's beacon
                if (received.ReceivedBytes > BeaconMax || Read(buffer.AsSpan(0, received.ReceivedBytes), _otherVersion) is not { } beacon) continue;
                Wait(beacon, from.Address);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            _deaf = !_stop.IsCancellationRequested; // the socket went away with its network: the owner opens a new one
        }
    }

    // Beacons are handed on together at most once per Timing.PcBeaconHandOver, so a busy network never floods the UI thread's queue.
    private void Wait(PcBeacon beacon, IPAddress from)
    {
        lock (_waiting)
        {
            var first = _waiting.Count == 0;
            _waiting[beacon.Id] = (beacon, from);
            if (!first) return;
        }
        _ = Guard.RunAsync("Other PCs: handing on beacons", async () =>
        {
            try
            {
                await Task.Delay(Timing.PcBeaconHandOver, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return; // stopped: nothing more is handed on
            }
            List<(PcBeacon, IPAddress)> heard;
            lock (_waiting)
            {
                heard = _waiting.Values.ToList();
                _waiting.Clear();
            }
            _heard(heard);
        });
    }

    /// A valid beacon of this version; one of another version only names its PC to <paramref name="otherVersion"/>.
    internal static PcBeacon? Read(ReadOnlySpan<byte> bytes, Action<string> otherVersion)
    {
        try
        {
            var beacon = JsonSerializer.Deserialize<PcBeacon>(bytes, PcProtocol.Json);
            if (beacon is { M: { } tag } && tag != PcProtocol.BeaconTag && tag.StartsWith(PcProtocol.BeaconPrefix, StringComparison.Ordinal) && PcIds.IsValid(beacon.Id))
            {
                otherVersion(PcIds.CleanName(beacon.Name));
                return null;
            }
            return beacon is not null && beacon.M == PcProtocol.BeaconTag && PcIds.IsValid(beacon.Id) && beacon.Port is > 0 and < 65536 ? beacon with { Name = PcIds.CleanName(beacon.Name) } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp?.Dispose();
        _stop.Dispose();
    }
}
