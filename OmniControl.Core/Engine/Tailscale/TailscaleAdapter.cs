using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OmniControl.Core.Engine.Tailscale;

/// Tailscale's network adapter on this PC and its Windows service: whether Windows still gives the adapter Tailscale's own address (it can fall back to a 169.254 one while Tailscale itself still answers), and the restart that brings it back.
public static class TailscaleAdapter
{
    private const string ServiceName = "Tailscale", AdapterName = "Tailscale", AdapterDescription = "Tailscale Tunnel";

    /// True when the Tailscale adapter has <paramref name="ownAddress"/> (Tailscale's reported own IPv4) as an address. The adapter is found by its description first, then by its name. Call off the UI thread.
    public static bool HasAddress(string ownAddress)
    {
        if (!IPAddress.TryParse(ownAddress, out var own) || !TailscaleStatusReader.IsTailscaleAddress(ownAddress)) return false;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(a => a.Description.Contains(AdapterDescription, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Name, AdapterName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => a.GetIPProperties().UnicastAddresses)
                .Any(u => u.Address.AddressFamily == AddressFamily.InterNetwork && u.Address.Equals(own));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    /// Restarts Tailscale's Windows service (Windows' administrator prompt unless OmniControl already is one). PowerShell without its
    /// profile, never cmd: an elevated cmd runs the person's AutoRun command. Call off the UI thread.
    public static HelperResult Restart() =>
        HelperProcess.RunElevated(SystemTools.PowerShell, $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Restart-Service -Name {ServiceName} -Force -ErrorAction Stop\"", hidden: true);
}
