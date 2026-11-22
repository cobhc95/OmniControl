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
    private volatile bool _networksFailed;
    private volatile FirewallReach _firewall; // what Windows Firewall does with OmniControl's ports, read only once a checklist shows it
    private volatile OtherVersion? _otherVersion; // the last PC heard on another protocol version
    private int _firewallWanted;
    private bool _networksFailedLogged, _firewallFailedLogged;
    private readonly HashSet<string> _skippedAdapters = new(); // logged once each (worker thread, under its own lock)

    private sealed record OtherVersion(string Name, DateTime At);

    /// A network other than Tailscale is set to Public in Windows, so other PCs can't find this one on it.
    public bool OnPublicNetwork => _networks?.OnPublic == true;

    /// This PC's network as other PCs meet it.
    private NetworkReach Network => _networks switch
    {
        null => _networksFailed ? NetworkReach.Failed : NetworkReach.NotRead,
        { Connected: false } => NetworkReach.None,
        { OnPublic: true } => NetworkReach.Public,
        { OnPrivate: true } => NetworkReach.Private,
        _ => NetworkReach.Unknown,
    };

    /// What "Can't reach it on this network?" says about this PC now (for a paired PC, also whether its key was read).
    public ReachFacts Reach(string? pcId = null) => new(Network, _firewall, PortsHeld, OtherVersionName, pcId is not null && !_secrets.ContainsKey(pcId));

    // A PC on another version is told about while its beacons keep coming.
    private string OtherVersionName => _otherVersion is { } heard && DateTime.UtcNow - heard.At < Timing.PcBeaconEvery * 3 ? heard.Name : string.Empty;

    // Discovery heard a PC on another protocol version (worker thread).
    private void HeardOtherVersion(string name)
    {
        var was = OtherVersionName;
        _otherVersion = new OtherVersion(name, DateTime.UtcNow);
        if (was != name) PcWork.Post(RaiseChanged);
    }

    /// A checklist shows: Windows' firewall is read now (off the UI thread) and again after every network change, never before; the networks
    /// are read too if nothing has yet (discovery, which reads them, may not be running).
    public void WatchReach()
    {
        if (Interlocked.Exchange(ref _firewallWanted, 1) == 0) Guard.OffUi("Other PCs: firewall", ReadFirewall);
        if (_networks is null && !_networksFailed) Guard.OffUi("Other PCs: reading the networks", RefreshNetworks);
    }

    private void ReadFirewall()
    {
        var was = _firewall;
        try
        {
            _firewall = PcFirewall.Check(Environment.ProcessPath ?? string.Empty, PcProtocol.TcpPort, PcProtocol.UdpPort) switch
            {
                FirewallVerdict.Allowed => FirewallReach.Allowed,
                FirewallVerdict.Blocked => FirewallReach.Blocked,
                _ => FirewallReach.NoRule,
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            if (!_firewallFailedLogged) Guard.Log("Other PCs: firewall", ex);
            _firewallFailedLogged = true;
            _firewall = FirewallReach.Failed;
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
        if (_firewall == FirewallReach.Blocked) _report("Windows Firewall still blocks OmniControl: remove its block rule in Windows Security.");
    }

    /// Reads the networks and their categories again (worker thread: COM and the adapters' list).
    public void RefreshNetworks()
    {
        PcNetworkView? now;
        try
        {
            now = PcNetworkView.Read(NetworkList.Read().Categories, SkippedAdapter);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NetworkInformationException)
        {
            if (!_networksFailedLogged) Guard.Log("Other PCs: network categories", ex);
            _networksFailedLogged = true;
            now = null;
        }
        var was = Network;
        _networksFailed = now is null;
        _networks = now;
        var changed = Network != was;
        if (_firewallWanted == 1) ReadFirewall(); // an adapter or its network may have changed what the firewall lets in
        if (changed) PcWork.Post(RaiseChanged);
    }

    private void SkippedAdapter(string name, Exception ex)
    {
        lock (_skippedAdapters)
            if (!_skippedAdapters.Add(name)) return;
        Guard.Log($"Other PCs: adapter {name} skipped", ex);
    }

    // A connection that came in on a Public network is dropped unanswered, unless it came over Tailscale.
    private bool AcceptsHere(TcpClient client) =>
        _networks is not { } networks
        || (client.Client.LocalEndPoint is IPEndPoint local && client.Client.RemoteEndPoint is IPEndPoint remote && networks.Accepts(local.Address, remote.Address));
}
