using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using OmniControl.App.Services;

namespace OmniControl.App.OtherPcs;

/// <summary>
/// Finding PCs on the local network: this PC sends a small broadcast (<see cref="PcBeacon"/>: id, name, port, whether pairing is open) every
/// <see cref="Timing.PcBeaconEvery"/>, and reports every one it hears from another PC. A beacon is not trusted for anything but "where to
/// dial": whoever answers must still prove the pairing key (<see cref="PcSession"/>) and a pairing needs the typed code.
/// </summary>
internal sealed class PcDiscovery : IDisposable
{
    private const int BeaconMax = 512;
    private readonly Func<PcBeacon> _own;
    private readonly Action<PcBeacon, IPAddress> _heard;
    private readonly CancellationTokenSource _stop = new();
    private UdpClient? _udp;

    /// <param name="own">What this PC says about itself now.</param>
    /// <param name="heard">Another PC's beacon and the address it came from (raised on a worker thread).</param>
    public PcDiscovery(Func<PcBeacon> own, Action<PcBeacon, IPAddress> heard)
    {
        _own = own;
        _heard = heard;
    }

    /// <summary>Opens the socket and starts announcing and listening; false (with the reason) when Windows or another program holds the port.</summary>
    public bool Start(out string? problem)
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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

    /// <summary>Announces now (a pairing was opened or closed).</summary>
    public void AnnounceNow() => Send();

    private async Task AnnounceAsync()
    {
        using var timer = new PeriodicTimer(Timing.PcBeaconEvery);
        try
        {
            do Send();
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Stopped: the normal end.
        }
    }

    private void Send()
    {
        if (_udp is not { } udp) return;
        var packet = JsonSerializer.SerializeToUtf8Bytes(_own(), PcProtocol.Json);
        foreach (var target in BroadcastAddresses())
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

    // The limited broadcast plus the directed broadcast of every connected IPv4 network, so a PC on a second adapter is heard too.
    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is not { } mask) continue;
                var address = unicast.Address.GetAddressBytes();
                var maskBytes = mask.GetAddressBytes();
                for (var i = 0; i < address.Length; i++) address[i] |= (byte)~maskBytes[i];
                yield return new IPAddress(address);
            }
        }
    }

    private async Task ListenAsync()
    {
        if (_udp is not { } udp) return;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                if (received.Buffer.Length > BeaconMax || Read(received.Buffer) is not { } beacon) continue;
                _heard(beacon, received.RemoteEndPoint.Address);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopped, or the socket went away with its network: nothing more to hear.
        }
    }

    private static PcBeacon? Read(byte[] bytes)
    {
        try
        {
            var beacon = JsonSerializer.Deserialize<PcBeacon>(bytes, PcProtocol.Json);
            return beacon is { M: PcProtocol.BeaconTag } && PcIds.IsValid(beacon.Id) && beacon.Port is > 0 and < 65536 ? beacon with { Name = PcIds.CleanName(beacon.Name) } : null;
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
