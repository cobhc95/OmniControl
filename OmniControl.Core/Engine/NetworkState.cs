using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmniControl.Core.Engine;

/// <summary>
/// Whether the PC is on a local network now: an adapter that is up, is not the loopback or a tunnel, and has a gateway (a router). That is
/// what a smart device on the home network needs; the internet itself is not asked for. Read on demand (the readiness wait asks it again
/// at each check), so nothing is subscribed or kept.
/// </summary>
public static class NetworkState
{
    public static bool IsUp()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable() && NetworkInterface.GetAllNetworkInterfaces().Any(HasRouter);
        }
        catch (NetworkInformationException)
        {
            return false; // Windows could not list the adapters: taken as "not up yet", and the wait asks again
        }
    }

    private static bool HasRouter(NetworkInterface adapter) =>
        adapter.OperationalStatus == OperationalStatus.Up
        && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && adapter.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
}
