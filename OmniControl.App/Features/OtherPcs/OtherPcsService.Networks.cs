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

    private const int FirewallNotRead = 0, FirewallAllows = 1, FirewallBlocks = 2;

    private volatile PcNetworkView? _networks; // null until read, or while Windows' list can't be read (then every network, as before)
    private volatile int _firewall; // what Windows Firewall does with OmniControl's port, read only once a checklist shows it
    private int _firewallWanted;
    private bool _networksFailedLogged, _firewallFailedLogged;

    /// A network other than Tailscale is set to Public in Windows, so other PCs can't find this one on it.
    public bool OnPublicNetwork => _networks?.OnPublic == true;

    /// This PC's network: false when Public, true when Private (or a domain's), null when Windows' list was not read or lists neither.
    public bool? NetworkIsPrivate => _networks is not { } networks ? null : networks.OnPublic ? false : networks.OnPrivate ? true : null;

    /// OmniControl is allowed through Windows Firewall here; null until read, or when Windows' firewall can't be read.
    public bool? FirewallAllowed => _firewall switch { FirewallAllows => true, FirewallBlocks => false, _ => null };

    /// A checklist shows the firewall row: Windows' firewall is read now (off the UI thread) and again after every network change, never before.
    public void WatchFirewall()
    {
        if (Interlocked.Exchange(ref _firewallWanted, 1) == 0) Guard.OffUi("Other PCs: firewall", ReadFirewall);
    }

    private void ReadFirewall()
    {
        var was = _firewall;
        try
        {
            _firewall = PcFirewall.IsAllowed(Environment.ProcessPath ?? string.Empty, PcProtocol.TcpPort) ? FirewallAllows : FirewallBlocks;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            if (!_firewallFailedLogged) Guard.Log("Other PCs: firewall", ex);
            _firewallFailedLogged = true;
            _firewall = FirewallNotRead;
        }
        if (_firewall != was) PcWork.Post(RaiseChanged);
    }

    /// The fix of a "Can't reach it on this network?" row (not Tailscale's, which its tab owns): the network row opens Windows' network settings, the firewall row adds the rule.
    public void RunReachFix(string checkId)
    {
        if (checkId == NetworkChecks.Firewall) Guard.OffUi("Other PCs: firewall rule", AllowOnPrivateNetworks);
        else if (checkId == NetworkChecks.Network && WebPage.Open(NetworkSettingsPage, _report) is { } problem) _report(problem);
    }

    // Private and domain networks only, never Public; a block rule (Windows' answer to a refused first-run prompt) wins over it and is for the person to remove.
    private void AllowOnPrivateNetworks()
    {
        try
        {
            PcFirewall.AllowPrivate(Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Guard.Log("Other PCs: firewall rule", ex);
            _report("Windows Firewall could not be changed. Allow OmniControl on Private networks in Windows Security instead.");
            return;
        }
        ReadFirewall();
        if (_firewall == FirewallBlocks) _report("Windows Firewall still blocks OmniControl: remove its block rule in Windows Security.");
    }

    /// Reads the networks and their categories again (worker thread: COM and the adapters' list).
    public void RefreshNetworks()
    {
        PcNetworkView? now;
        try
        {
            now = PcNetworkView.Read(NetworkList.Read().Categories);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NetworkInformationException)
        {
            if (!_networksFailedLogged) Guard.Log("Other PCs: network categories", ex);
            _networksFailedLogged = true;
            now = null;
        }
        var was = NetworkIsPrivate;
        _networks = now;
        var changed = NetworkIsPrivate != was;
        if (_firewallWanted == 1) ReadFirewall(); // an adapter or its network may have changed what the firewall lets in
        if (changed) PcWork.Post(RaiseChanged);
    }

    // A connection that came in on a Public network is dropped unanswered, unless it came over Tailscale.
    private bool AcceptsHere(TcpClient client) =>
        _networks is not { } networks
        || (client.Client.LocalEndPoint is IPEndPoint local && client.Client.RemoteEndPoint is IPEndPoint remote && networks.Accepts(local.Address, remote.Address));
}
