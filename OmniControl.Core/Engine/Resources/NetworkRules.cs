using System.Management;
using Microsoft.CSharp.RuntimeBinder;

namespace OmniControl.Core.Engine.Resources;

/// What OmniControl asks Windows to do with one program's traffic: an upload cap in bits per second (null: none) and whether it is blocked
/// (download limits are <see cref="DownloadShaper"/>'s).
public sealed record NetworkRule(string AppId, string Path, ulong? UploadBits, bool Blocked);

/// One of OmniControl's rules as Windows holds it (or should): a throttle policy when <see cref="Bits"/> is set, else one direction of a
/// firewall block. <see cref="Program"/> is the policy's program name or the firewall rule's path.
internal sealed record HeldRule(string Name, string Program, ulong? Bits = null, bool Inbound = false);

/// What must change so Windows holds exactly the wanted rules: every rule of a name in <see cref="Remove"/> goes, <see cref="Add"/> goes in.
internal sealed record RulePlan(IReadOnlyList<HeldRule> Remove, IReadOnlyList<HeldRule> Add)
{
    public bool None => Remove.Count == 0 && Add.Count == 0;
}

/// Puts per-program network rules into Windows and takes them out again: an upload cap is a QoS throttle policy (the class behind
/// New-NetQosPolicy, MSFT_NetQosPolicySettingData, in the ActiveStore so it ends with a restart at the latest), a block is a pair of Windows
/// Firewall rules (in and out) in OmniControl's own rule group. Every sync reads what Windows holds of OmniControl's (by <see cref="Prefix"/>),
/// so a rule an earlier or crashed run left is found too, changes only what differs and reads Windows again to prove it. Slow calls (WMI,
/// COM): run on a worker, never on the UI thread.
public sealed class NetworkRules
{
    public const string Prefix = "OmniControl ";
    private const string FirewallGroup = "OmniControl", QosClass = "MSFT_NetQosPolicySettingData", QosScope = @"\\.\root\StandardCimv2";
    private const int FirewallBlock = 0, FirewallIn = 1, FirewallOut = 2, AllProfiles = 0x7FFFFFFF;
    private const sbyte NoMarking = -1;
    private const ulong RateSlack = 8 * 1024; // Windows may keep a rate rounded to its own unit: within one KB/s is the same rate

    /// True once a sync left none of OmniControl's rules in Windows (and nothing failed), so shut-down has nothing to take out.
    public bool HoldsNone { get; private set; }

    /// Makes Windows hold exactly <paramref name="wanted"/> of OmniControl's rules, then reads it back; returns one sentence per failure,
    /// and one when Windows still holds or lacks a rule after the change.
    public IReadOnlyList<string> Sync(IReadOnlyList<NetworkRule> wanted)
    {
        var problems = new List<string>();
        var plan = Plan(wanted, Held(problems));
        foreach (var gone in plan.Remove) Try(problems, $"take “{gone.Name}” out of Windows", () => Remove(gone));
        foreach (var rule in plan.Add) Try(problems, $"put “{rule.Name}” into Windows", () => Add(rule));
        if (!plan.None && problems.Count == 0 && Plan(wanted, Held(problems)) is { None: false } left) problems.Add(Mismatch(left));
        HoldsNone = wanted.Count == 0 && problems.Count == 0;
        return problems;
    }

    /// Takes out every rule OmniControl ever set (by its name), also ones a crashed run left. Returns the failures.
    public IReadOnlyList<string> RemoveAll() => Sync(Array.Empty<NetworkRule>());

    /// The changes from what Windows holds (<paramref name="held"/>) to <paramref name="wanted"/>: a name held twice, or held unlike its wanted
    /// rule, or not wanted at all, is removed (each of its rules), and a wanted rule not held exactly is added.
    internal static RulePlan Plan(IReadOnlyList<NetworkRule> wanted, IReadOnlyList<HeldRule> held)
    {
        var want = wanted.SelectMany(Wanted).ToList();
        var remove = held.GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 || !want.Any(w => Same(w, g.First()))).Select(g => g.First()).ToList();
        var add = want.Where(w => !held.Any(h => Named(h, w.Name)) || remove.Any(r => Named(r, w.Name))).ToList();
        return new RulePlan(remove, add);
    }

    private static IEnumerable<HeldRule> Wanted(NetworkRule rule)
    {
        if (rule.UploadBits is { } bits) yield return new HeldRule($"{Prefix}limit {rule.AppId}", Path.GetFileName(rule.Path.Length > 0 ? rule.Path : rule.AppId + ".exe"), bits);
        if (!rule.Blocked) yield break;
        yield return new HeldRule($"{Prefix}block {rule.AppId} (in)", rule.Path, Inbound: true);
        yield return new HeldRule($"{Prefix}block {rule.AppId} (out)", rule.Path);
    }

    private static bool Named(HeldRule rule, string name) => string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool Same(HeldRule want, HeldRule held) => Named(held, want.Name) && want.Inbound == held.Inbound
        && string.Equals(Environment.ExpandEnvironmentVariables(want.Program), Environment.ExpandEnvironmentVariables(held.Program), StringComparison.OrdinalIgnoreCase)
        && (want.Bits, held.Bits) switch { (null, null) => true, ({ } a, { } b) => (a > b ? a - b : b - a) < RateSlack, _ => false };

    private static string Mismatch(RulePlan left) => "Windows does not hold the network rules as set"
        + (left.Remove.Count > 0 ? $"; still there: {string.Join(", ", left.Remove.Select(r => r.Name))}" : string.Empty)
        + (left.Add.Count > 0 ? $"; missing: {string.Join(", ", left.Add.Select(r => r.Name))}" : string.Empty) + ".";

    /// OmniControl's rules in Windows now; a kind that cannot be read adds its failure and counts as holding none.
    private static List<HeldRule> Held(List<string> problems)
    {
        var held = new List<HeldRule>();
        Try(problems, "read the upload limits", () => EachQos((policy, name) =>
            held.Add(new HeldRule(name, policy["AppPathNameMatchCondition"] as string ?? string.Empty, Convert.ToUInt64(policy["ThrottleRateAction"] ?? 0UL)))));
        Try(problems, "read the blocks", () => Firewall(rules =>
        {
            foreach (var rule in (dynamic)rules)
                try
                {
                    if ((string?)rule.Grouping == FirewallGroup && (string?)rule.Name is { } name)
                        held.Add(new HeldRule(name, (string?)rule.ApplicationName ?? string.Empty, Inbound: (int)rule.Direction == FirewallIn));
                }
                finally { Release((object)rule); }
        }));
        return held;
    }

    private static void Try(List<string> problems, string what, Action work)
    {
        try { work(); }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or RuntimeBinderException or ArgumentException or InvalidCastException)
        {
            problems.Add($"Couldn't {what}: {ex.Message}");
        }
    }

    private static void Add(HeldRule rule)
    {
        if (rule.Bits is { } bits) AddQos(rule.Name, rule.Program, bits);
        else AddBlock(rule);
    }

    /// Takes out every rule of this name (a duplicate too).
    private static void Remove(HeldRule rule)
    {
        if (rule.Bits is not null)
        {
            EachQos((policy, name) =>
            {
                if (Named(rule, name)) policy.Delete(new DeleteOptions { Context = ActiveStore() });
            });
            return;
        }
        Firewall(rules =>
        {
            var names = new List<string>();
            foreach (var found in (dynamic)rules)
                try
                {
                    if ((string?)found.Grouping == FirewallGroup && (string?)found.Name is { } name && Named(rule, name)) names.Add(name);
                }
                finally { Release((object)found); }
            foreach (var name in names) ((dynamic)rules).Remove(name);
        });
    }

    // ---- QoS throttle policies ----

    private static ManagementNamedValueCollection ActiveStore() => new() { { "PolicyStore", "ActiveStore" } };

    private static void AddQos(string name, string program, ulong bits)
    {
        using var policies = new ManagementClass(new ManagementScope(QosScope), new ManagementPath(QosClass), new ObjectGetOptions(ActiveStore()));
        using var policy = policies.CreateInstance();
        policy["Name"] = name;
        policy["AppPathNameMatchCondition"] = program;
        policy["ThrottleRateAction"] = bits;
        policy["DSCPAction"] = NoMarking;
        policy["PriorityValue8021Action"] = NoMarking;
        policy.Put(new PutOptions(ActiveStore()) { Type = PutType.CreateOnly });
    }

    /// Hands each OmniControl policy in the ActiveStore and its name (as Windows has it) to <paramref name="each"/>.
    private static void EachQos(Action<ManagementObject, string> each)
    {
        var query = new ObjectQuery($"SELECT * FROM {QosClass} WHERE Name LIKE '{Prefix}%'");
        using var searcher = new ManagementObjectSearcher(new ManagementScope(QosScope), query, new System.Management.EnumerationOptions { Context = ActiveStore() });
        using var found = searcher.Get();
        foreach (var policy in found.OfType<ManagementObject>())
            using (policy)
                if (policy["Name"] is string name && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) each(policy, name);
    }

    // ---- Firewall blocks ----

    private static dynamic Com(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    /// Lets go of a firewall COM object at once (its runtime wrapper would hold it until a garbage collection).
    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    /// Runs <paramref name="work"/> on Windows Firewall's rule list, letting go of both COM objects after.
    private static void Firewall(Action<object> work)
    {
        var policy = Com("HNetCfg.FwPolicy2");
        object? rules = null;
        try
        {
            rules = policy.Rules;
            work(rules!);
        }
        finally
        {
            Release(rules);
            Release((object)policy);
        }
    }

    private static void AddBlock(HeldRule rule)
    {
        if (rule.Program.Length == 0) throw new ArgumentException("Windows did not say where the program is, so the firewall cannot name it.");
        Firewall(rules =>
        {
            var block = Com("HNetCfg.FWRule");
            try
            {
                block.Name = rule.Name;
                block.Grouping = FirewallGroup;
                block.ApplicationName = rule.Program;
                block.Action = FirewallBlock;
                block.Direction = rule.Inbound ? FirewallIn : FirewallOut;
                block.Profiles = AllProfiles;
                block.Enabled = true;
                ((dynamic)rules).Add(block);
            }
            finally { Release((object)block); }
        });
    }
}
