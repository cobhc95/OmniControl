using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OmniControl.App.Features.OtherPcs;

// Which networks other PCs may find and reach this one on: Windows' Private (and domain) networks and Tailscale, never a Public one.
public sealed partial class OtherPcsService
{
    /// The hardware watch that reads the networks again when Windows announces a change.
    public const string NetworksWatchId = "otherpcs.networks";

    public const string FindTip = "Both PCs need to be on a network set to Private in Windows, or connected through Tailscale.";
    public const string PublicLine = "This network is set to Public in Windows, so other PCs can't find this one here. Set it to Private in Windows' network settings, or use Tailscale.";
    public const string NetworkSettingsPage = "ms-settings:network-status";

    private volatile PcNetworkView? _networks; // null until read, or while Windows' list can't be read (then every network, as before)
    private bool _networksFailedLogged;

    /// A network other than Tailscale is set to Public in Windows, so other PCs can't find this one on it.
    public bool OnPublicNetwork => _networks?.OnPublic == true;

    /// Reads the networks and their categories again (worker thread: COM and the adapters' list).
    public void RefreshNetworks()
    {
        PcNetworkView? now;
        try
        {
            now = PcNetworkView.Read(NetworkCategories.Read());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NetworkInformationException)
        {
            if (!_networksFailedLogged) Guard.Log("Other PCs: network categories", ex);
            _networksFailedLogged = true;
            now = null;
        }
        var wasPublic = OnPublicNetwork;
        _networks = now;
        if (OnPublicNetwork != wasPublic) PcWork.Post(RaiseChanged);
    }

    // A connection that came in on a Public network is dropped unanswered, unless it came over Tailscale.
    private bool AcceptsHere(TcpClient client) =>
        _networks is not { } networks
        || (client.Client.LocalEndPoint is IPEndPoint local && client.Client.RemoteEndPoint is IPEndPoint remote && networks.Accepts(local.Address, remote.Address));
}
