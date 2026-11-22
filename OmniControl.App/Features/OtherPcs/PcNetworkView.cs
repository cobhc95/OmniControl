using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// One connected adapter as Other PCs sees it: its addresses, its Windows network category (null: Windows did not list it), Tailscale, and
/// whether it has a default gateway (the network this PC is really on, not a Hyper-V, WSL or VPN side adapter).
internal sealed record PcAdapter(int Index, IReadOnlyList<IPAddress> Addresses, IReadOnlyList<IPAddress> Broadcasts, NetworkCategory? Category, bool Tailscale, bool Gateway = false)
{
    /// Other PCs may find this one and connect here only on a network set to Private (or a domain's) in Windows, or over Tailscale.
    public bool Allowed => Tailscale || Category is NetworkCategory.Private or NetworkCategory.Domain;
}

/// Which networks other PCs may find and reach this one on (pure decisions over a list of adapters, so they can be tested).
internal sealed record PcNetworkView(IReadOnlyList<PcAdapter> Adapters)
{
    /// A connection that came in to <paramref name="local"/> from <paramref name="remote"/>: on an allowed adapter, over Tailscale or this PC itself.
    public bool Accepts(IPAddress local, IPAddress remote)
    {
        if (IPAddress.IsLoopback(local) || TailscaleStatusReader.IsTailscaleAddress(remote.ToString())) return true;
        if (local.IsIPv4MappedToIPv6) local = local.MapToIPv4();
        return Adapters.Any(a => a.Allowed && a.Addresses.Contains(local));
    }

    /// A beacon that arrived on the adapter with this index from <paramref name="remote"/>.
    public bool HearsFrom(int index, IPAddress remote) =>
        TailscaleStatusReader.IsTailscaleAddress(remote.ToString()) || Adapters.Any(a => a.Allowed && a.Index == index);

    /// Where beacons go: each allowed network's broadcast, and the limited broadcast only while no connected network is closed to them
    /// (Windows sends it out of whichever adapter it likes).
    public IReadOnlyList<IPAddress> Broadcasts()
    {
        var targets = Adapters.Where(a => a.Allowed).SelectMany(a => a.Broadcasts).ToList();
        if (Adapters.Any(a => a.Allowed) && Adapters.All(a => a.Allowed)) targets.Insert(0, IPAddress.Broadcast); // also when no adapter gave its mask
        return targets;
    }

    /// A network other than Tailscale is set to Public in Windows.
    public bool OnPublic => Main.Any(a => a.Category is NetworkCategory.Public);

    /// A network other than Tailscale is set to Private (or is a domain's) in Windows.
    public bool OnPrivate => Main.Any(a => a.Allowed);

    /// This PC is on a network at all (Tailscale aside).
    public bool Connected => Adapters.Any(a => !a.Tailscale);

    // The adapters with a gateway when there are any: a Hyper-V or WSL switch Windows calls Public must not hide the Private network beside it.
    private IEnumerable<PcAdapter> Main
    {
        get
        {
            var lan = Adapters.Where(a => !a.Tailscale).ToList();
            return lan.Any(a => a.Gateway) ? lan.Where(a => a.Gateway) : lan;
        }
    }

    /// The connected IPv4 adapters now, with the categories Windows gave (read off the UI thread); an adapter that can't be read is skipped and
    /// handed to <paramref name="skipped"/> (its name and why), never the whole list.
    public static PcNetworkView Read(IReadOnlyDictionary<Guid, NetworkCategory> categories, Action<string, Exception> skipped) =>
        From(NetworkInterface.GetAllNetworkInterfaces(), categories, skipped);

    internal static PcNetworkView From(IEnumerable<NetworkInterface> nics, IReadOnlyDictionary<Guid, NetworkCategory> categories, Action<string, Exception> skipped)
    {
        var adapters = new List<PcAdapter>();
        foreach (var nic in nics)
        {
            try
            {
                if (AdapterOf(nic, categories) is { } adapter) adapters.Add(adapter);
            }
            // An adapter with IPv4 switched off (Hyper-V switches, some VPNs) throws WSAEPROTONOSUPPORT; a driver going away mid-read throws too.
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException)
            {
                skipped(nic.Description, ex);
            }
        }
        return new PcNetworkView(adapters);
    }

    private static PcAdapter? AdapterOf(NetworkInterface nic, IReadOnlyDictionary<Guid, NetworkCategory> categories)
    {
        if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) return null;
        var props = nic.GetIPProperties();
        var v4 = props.GetIPv4Properties();
        var addresses = new List<IPAddress>();
        var broadcasts = new List<IPAddress>();
        foreach (var unicast in props.UnicastAddresses)
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            addresses.Add(unicast.Address);
            if (unicast.IPv4Mask is not { } mask) continue;
            var bytes = unicast.Address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            for (var i = 0; i < bytes.Length; i++) bytes[i] |= (byte)~maskBytes[i];
            broadcasts.Add(new IPAddress(bytes));
        }
        if (addresses.Count == 0) return null; // IPv6 only: beacons are IPv4
        NetworkCategory? category = Guid.TryParse(nic.Id, out var id) && categories.TryGetValue(id, out var found) ? found : null;
        var tailscale = TailscaleAdapter.IsTailscale(nic) || addresses.Any(a => TailscaleStatusReader.IsTailscaleAddress(a.ToString()));
        var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
        return new PcAdapter(v4.Index, addresses, broadcasts, category, tailscale, gateway);
    }
}