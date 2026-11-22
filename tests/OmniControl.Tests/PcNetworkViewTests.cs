using System.Net;
using System.Net.NetworkInformation;
using OmniControl.App.Features.OtherPcs;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// Other PCs find and reach this one only on Private (or domain) networks and over Tailscale.
public class PcNetworkViewTests
{
    private static PcAdapter Lan(int index, string address, string broadcast, NetworkCategory? category) =>
        new(index, new[] { IPAddress.Parse(address) }, new[] { IPAddress.Parse(broadcast) }, category, Tailscale: false);

    private static readonly PcAdapter Home = Lan(1, "192.168.1.10", "192.168.1.255", NetworkCategory.Private);
    private static readonly PcAdapter Cafe = Lan(2, "10.0.0.5", "10.0.0.255", NetworkCategory.Public);
    private static readonly PcAdapter Tailnet = new(3, new[] { IPAddress.Parse("100.101.102.103") }, Array.Empty<IPAddress>(), NetworkCategory.Public, Tailscale: true);

    [Fact]
    public void PrivateOnly_BroadcastsEverywhere()
    {
        var view = new PcNetworkView(new[] { Home });
        Assert.Equal(new[] { IPAddress.Broadcast, IPAddress.Parse("192.168.1.255") }, view.Broadcasts());
        Assert.False(view.OnPublic);
    }

    [Fact]
    public void PublicNetwork_NoBeaconsThere_AndNoLimitedBroadcast()
    {
        var view = new PcNetworkView(new[] { Home, Cafe, Tailnet });
        Assert.Equal(new[] { IPAddress.Parse("192.168.1.255") }, view.Broadcasts());
        Assert.True(view.OnPublic);
        Assert.True(view.HearsFrom(1, IPAddress.Parse("192.168.1.20")));
        Assert.False(view.HearsFrom(2, IPAddress.Parse("10.0.0.7")));
    }

    [Fact]
    public void Connections_AcceptedOnPrivateTailscaleOrLoopbackOnly()
    {
        var view = new PcNetworkView(new[] { Home, Cafe, Tailnet });
        Assert.True(view.Accepts(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("192.168.1.20")));
        Assert.False(view.Accepts(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("10.0.0.7")));
        Assert.True(view.Accepts(IPAddress.Parse("100.101.102.103"), IPAddress.Parse("100.64.0.9")));
        Assert.True(view.Accepts(IPAddress.Loopback, IPAddress.Loopback));
        Assert.False(view.Accepts(IPAddress.Parse("172.16.0.1"), IPAddress.Parse("172.16.0.2"))); // an adapter Windows did not list
    }

    [Fact]
    public void UnlistedCategory_IsNotTrusted() => Assert.False(Lan(4, "10.1.1.1", "10.1.1.255", null).Allowed);

    [Fact]
    public void TailscaleOnly_IsNotCalledPublic() => Assert.False(new PcNetworkView(new[] { Tailnet }).OnPublic);

    [Fact]
    public void HyperVSwitchCalledPublic_DoesNotHideThePrivateNetworkWithTheGateway()
    {
        var view = new PcNetworkView(new[] { Home with { Gateway = true }, Cafe });
        Assert.False(view.OnPublic);
        Assert.True(view.OnPrivate);
    }

    [Fact]
    public void AdapterWithoutMask_StillGetsTheLimitedBroadcast() =>
        Assert.Equal(new[] { IPAddress.Broadcast }, new PcNetworkView(new[] { Home with { Broadcasts = Array.Empty<IPAddress>() } }).Broadcasts());

    [Fact]
    public void AdaptersThatThrow_AreSkippedAndNamed_NotTheWholeView()
    {
        var skipped = new List<string>();
        var view = PcNetworkView.From(new NetworkInterface[] { new BrokenNic("No IPv4", new BrokenProps()), new BrokenNic("Gone", null) },
            new Dictionary<Guid, NetworkCategory>(), (name, _) => skipped.Add(name));
        Assert.Empty(view.Adapters);
        Assert.False(view.Connected);
        Assert.Equal(new[] { "No IPv4", "Gone" }, skipped);
    }

    // An up adapter whose properties throw as Windows does: WSAEPROTONOSUPPORT for IPv4 switched off, or the whole read when it went away.
    private sealed class BrokenNic(string name, IPInterfaceProperties? props) : NetworkInterface
    {
        public override string Id => Guid.Empty.ToString();
        public override string Description => name;
        public override OperationalStatus OperationalStatus => OperationalStatus.Up;
        public override NetworkInterfaceType NetworkInterfaceType => NetworkInterfaceType.Ethernet;
        public override IPInterfaceProperties GetIPProperties() => props ?? throw new NetworkInformationException(1168);
    }

    private sealed class BrokenProps : IPInterfaceProperties
    {
        public override IPv4InterfaceProperties GetIPv4Properties() => throw new NetworkInformationException(10043);
        public override IPv6InterfaceProperties GetIPv6Properties() => throw new NetworkInformationException(10043);
        public override IPAddressInformationCollection AnycastAddresses => throw new NotSupportedException();
        public override IPAddressCollection DhcpServerAddresses => throw new NotSupportedException();
        public override IPAddressCollection DnsAddresses => throw new NotSupportedException();
        public override string DnsSuffix => string.Empty;
        public override GatewayIPAddressInformationCollection GatewayAddresses => throw new NotSupportedException();
        public override bool IsDnsEnabled => false;
        public override bool IsDynamicDnsEnabled => false;
        public override MulticastIPAddressInformationCollection MulticastAddresses => throw new NotSupportedException();
        public override UnicastIPAddressInformationCollection UnicastAddresses => throw new NotSupportedException();
        public override IPAddressCollection WinsServersAddresses => throw new NotSupportedException();
    }
}