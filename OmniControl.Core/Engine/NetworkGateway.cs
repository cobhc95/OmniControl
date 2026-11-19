using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmniControl.Core.Engine;

/// The router's address: the IPv4 default gateway of the first network connection that is up and has one.
public static class NetworkGateway
{
    /// The gateway's address (<c>192.168.0.1</c>), or null when no connection has one.
    public static string? FindDefault()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            return null; // Windows could not list the connections: the guide just has no address to open
        }
    }
}
