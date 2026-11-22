using Microsoft.CSharp.RuntimeBinder;

namespace OmniControl.Core.Engine;

/// What Windows Firewall does with a program's ports on the networks this PC is on now.
public enum FirewallVerdict { Allowed, NoRule, Blocked }

/// Windows Firewall as other PCs meet it: reading whether they get through to a program's TCP and UDP ports on the networks this PC is on now (off on all of
/// them, or an enabled inbound allow rule for the program or the port, and no inbound block rule for either; a block wins, as in Windows), and
/// the one rule that opens the program on Private and Domain networks, never Public. Slow COM calls: run on a worker, never on the UI thread.
/// Every call throws <see cref="COMException"/>, <see cref="InvalidCastException"/> or <see cref="UnauthorizedAccessException"/> when Windows' firewall can't be read or changed.
public static class PcFirewall
{
    /// Not in OmniControl's block group, so the blocks' sync never takes it out; Setup adds and removes the same rule by this name.
    public const string RuleName = "OmniControl on private networks";

    private const int Inbound = 1, Allow = 1, Tcp = 6, Udp = 17, AnyProtocol = 256;
    private const int Domain = 1, Private = 2, Public = 4;

    /// Whether other PCs get through to <paramref name="tcpPort"/> and <paramref name="udpPort"/> of <paramref name="program"/>: allowed only when both are.
    public static FirewallVerdict Check(string program, int tcpPort, int udpPort) => WithRules((policy, rules) =>
    {
        var current = (int)policy.CurrentProfileTypes;
        if (!AnyProfileOn(policy, current)) return FirewallVerdict.Allowed;
        bool tcp = false, udp = false;
        foreach (var rule in rules)
            try
            {
                var onTcp = Verdict(rule, current, program, Tcp, tcpPort);
                var onUdp = Verdict(rule, current, program, Udp, udpPort);
                if (onTcp is false || onUdp is false) return FirewallVerdict.Blocked;
                tcp |= onTcp is true;
                udp |= onUdp is true;
            }
            finally { Release((object)rule); }
        return tcp && udp ? FirewallVerdict.Allowed : FirewallVerdict.NoRule;
    });

    /// Makes the one inbound allow rule for <paramref name="program"/> (any port, TCP and UDP) on Private and Domain networks, replacing an earlier
    /// one of the name. Needs administrator rights (OmniControl's own, or none).
    public static void AllowPrivate(string program) => WithRules((policy, rules) =>
    {
        var old = new List<string>();
        foreach (var found in rules)
            try
            {
                if ((string?)found.Name is { } name && string.Equals(name, RuleName, StringComparison.OrdinalIgnoreCase)) old.Add(name);
            }
            finally { Release((object)found); }
        foreach (var name in old) rules.Remove(name);
        var rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        try
        {
            dynamic allow = rule;
            allow.Name = RuleName;
            allow.Description = "Lets other PCs find and reach OmniControl on Private networks.";
            allow.ApplicationName = program;
            allow.Action = Allow;
            allow.Direction = Inbound;
            allow.Protocol = AnyProtocol;
            allow.Profiles = Private | Domain;
            allow.Enabled = true;
            rules.Add(allow);
        }
        finally { Release(rule); }
        return true;
    });

    /// Runs <paramref name="work"/> on the firewall policy and its rule list, letting go of both COM objects after.
    private static T WithRules<T>(Func<dynamic, dynamic, T> work)
    {
        var policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
        object? rules = null;
        try
        {
            dynamic firewall = policy;
            rules = firewall.Rules;
            return work(firewall, rules!);
        }
        catch (RuntimeBinderException ex)
        {
            throw new InvalidCastException("Windows Firewall answered in a form OmniControl does not read.", ex);
        }
        finally
        {
            Release(rules);
            Release(policy);
        }
    }

    private static bool AnyProfileOn(dynamic policy, int current)
    {
        foreach (var profile in new[] { Domain, Private, Public })
            if ((current & profile) != 0 && (bool)policy.FirewallEnabled[profile]) return true;
        return false;
    }

    /// True: an allow rule that covers the program's port of that protocol here; false: a block rule that does; null: the rule has nothing to say about it.
    private static bool? Verdict(dynamic rule, int current, string program, int protocol, int port)
    {
        if (!(bool)rule.Enabled || (int)rule.Direction != Inbound || ((int)rule.Profiles & current) == 0) return null;
        var ruleProtocol = (int)rule.Protocol;
        if ((ruleProtocol != protocol && ruleProtocol != AnyProtocol) || !PortListed((string?)rule.LocalPorts, port)) return null;
        var application = (string?)rule.ApplicationName;
        var covers = string.IsNullOrEmpty(application) || string.Equals(Environment.ExpandEnvironmentVariables(application), program, StringComparison.OrdinalIgnoreCase);
        return covers ? (int)rule.Action == Allow : null;
    }

    /// A firewall rule's local ports ("*", "80,443", "5000-5010"; empty means every port) include <paramref name="port"/>.
    internal static bool PortListed(string? ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports) || ports.Trim() == "*") return true;
        foreach (var part in ports.Split(','))
        {
            var range = part.Split('-');
            if (range.Length == 1 && int.TryParse(range[0], out var one) && one == port) return true;
            if (range.Length == 2 && int.TryParse(range[0], out var low) && int.TryParse(range[1], out var high) && port >= low && port <= high) return true;
        }
        return false;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}
