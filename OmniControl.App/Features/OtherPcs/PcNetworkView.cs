using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// One connected adapter as Other PCs sees it: its addresses, its Windows network category (null: Windows did not list it) and Tailscale.
internal sealed record PcAdapter(int Index, IReadOnlyList<IPAddress> Addresses, IReadOnlyList<IPAddress> Broadcasts, NetworkCategory? Category, bool Tailscale)
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
        if (targets.Count > 0 && Adapters.All(a => a.Allowed)) targets.Insert(0, IPAddress.Broadcast);
        return targets;
    }

    /// A network other than Tailscale is set to Public in Windows.
    public bool OnPublic => Adapters.Any(a => !a.Tailscale && a.Category is NetworkCategory.Public);

    /// The connected IPv4 adapters now, with the categories Windows gave (read off the UI thread).
    public static PcNetworkView Read(IReadOnlyDictionary<Guid, NetworkCategory> categories)
    {
        var adapters = new List<PcAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            if (props.GetIPv4Properties() is not { } v4) continue;
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
            NetworkCategory? category = Guid.TryParse(nic.Id, out var id) && categories.TryGetValue(id, out var found) ? found : null;
            var tailscale = TailscaleAdapter.IsTailscale(nic) || addresses.Any(a => TailscaleStatusReader.IsTailscaleAddress(a.ToString()));
            adapters.Add(new PcAdapter(v4.Index, addresses, broadcasts, category, tailscale));
        }
        return new PcNetworkView(adapters);
    }
}
