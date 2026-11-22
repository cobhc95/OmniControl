using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic;

/// What the Network traffic control page and its rows use of the programs' network rules: made real by <see cref="NetworkLimits"/>, which
/// makes Windows hold them; a stand-in given to the page leaves Windows alone.
public interface INetworkLimits
{
    /// The rule of a program, or null when it has none.
    AppNetRule? Rule(string appId);

    /// Changes one program's rule and saves.
    void Set(string appId, Action<AppNetRule> change);

    /// The "Packet filter" switch, and changing it.
    bool PacketFilterOn { get; }
    void SetPacketFilter(bool on);

    /// Where the work runs changed, or the service's state (a worker).
    event Action? Changed;

    bool Available { get; }
    bool CanInstallService { get; }
    string UnavailableLine { get; }
    NetworkLimits.ServiceState Service { get; }
    NetworkLimits.DownloadState Download { get; }

    void InstallPacketFilter();
    Task InstallServiceAsync();

    /// The page came on screen: where the work runs and what the service holds are looked at again.
    void Recheck();
}
