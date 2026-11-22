using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic;

/// What the Network traffic control page and its rows use of the programs' network rules: made real by <see cref="NetworkLimits"/>, which
/// makes Windows hold them; a stand-in given to the page leaves Windows alone.
public interface INetworkLimits
{
    /// The rule of a program in force at a place, or null when it has none.
    AppNetRule? Rule(NetScope scope, string appId);

    /// Changes programs' rules at a place and saves once (a place's first edit gives it its own rules, copied from those it used).
    void Set(NetScope scope, IEnumerable<string> appIds, Action<AppNetRule> change);

    /// True when the place has rules of its own.
    bool HasOwn(NetScope scope);

    /// The place uses its parent's rules again.
    void UseParent(NetScope scope);

    /// The place's automation rule, which puts its own rules in effect (null: none, deleted, or automation off).
    Shared.Contracts.FeatureRuleView? RuleOf(NetScope place);

    /// True while the place's own rules are used.
    bool InEffect(NetScope place);

    /// Opens Automation where the place's rule is.
    void EditRule(NetScope place);

    /// One sentence while some upload limit cannot follow its adapter exactly, else null.
    string? UploadScopeLine { get; }

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
