using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Features.Traffic;

/// Whose rules an edit or a row reads: All adapters (<see cref="All"/>), one adapter's (<see cref="Network"/> empty) or one network's on one adapter.
public sealed record NetScope(string Adapter, string Network)
{
    public static readonly NetScope All = new(string.Empty, string.Empty);

    public static NetScope Of(string adapter) => new(adapter, string.Empty);

    public bool IsAll => Adapter.Length == 0;
    public bool IsNetwork => Network.Length > 0;
}

/// The programs' rules by place and what Windows must hold for them (pure, so tests check it). A place's own rules are used only while they are in
/// effect (their automation rule holds: <see cref="NetworkLimits"/>), and the most specific in effect wins: a network's own rules while an adapter is
/// on it, else the adapter's own, else All adapters'. Blocks and download limits follow each connected adapter; upload
/// limits are Windows' throttle rules, which follow only a network's category, so adapters of one category that differ use All adapters'.
internal static class ScopedRules
{
    /// The rules a place has of its own, or null when it uses its parent's.
    public static AppNetScope? Own(TrafficSettings setup, NetScope scope) =>
        scope.IsAll ? null : setup.Scoped.FirstOrDefault(s => s.Adapter == scope.Adapter && s.Network == scope.Network);

    /// The rules in force at a place: its own, else its adapter's, else All adapters'.
    public static List<AppNetRule> RulesOf(TrafficSettings setup, NetScope scope) =>
        Own(setup, scope)?.Apps ?? (scope.IsNetwork ? RulesOf(setup, NetScope.Of(scope.Adapter)) : setup.Apps);

    /// The rules used at a place now: its own while in effect, else its adapter's in effect, else All adapters'.
    private static List<AppNetRule> InForce(TrafficSettings setup, NetScope scope, IReadOnlySet<NetScope> inEffect) =>
        (inEffect.Contains(scope) ? Own(setup, scope)?.Apps : null) ?? (scope.IsNetwork ? InForce(setup, NetScope.Of(scope.Adapter), inEffect) : setup.Apps);

    /// The place an adapter's or network's own rules are for.
    public static NetScope Of(AppNetScope scope) => new(scope.Adapter, scope.Network);

    /// What Windows must hold with these places' own rules in effect, and whether some upload limit could not follow its adapter exactly.
    public static (IReadOnlyList<NetworkRule> Rules, bool UploadInexact) Wanted(TrafficSettings setup, NetworkSnapshot now, IReadOnlySet<NetScope> inEffect)
    {
        if (!setup.Scoped.Any(s => inEffect.Contains(Of(s)))) return (setup.Apps.Select(r => Rule(r.Id, r.Path, Ask.Of(r))).OfType<NetworkRule>().ToList(), false);
        var live = now.Adapters.Where(a => a.IsUp).Select(a => (Adapter: a, Network: now.On(a.Id))).Select(l =>
            (l.Adapter, Category: l.Network?.Category ?? NetworkCategory.Public, Rules: InForce(setup, new NetScope(l.Adapter.Id, l.Network?.Id ?? string.Empty), inEffect))).ToList();
        var everyRule = setup.Apps.Concat(setup.Scoped.SelectMany(s => s.Apps)).ToList();
        var rules = new List<NetworkRule>();
        var inexact = false;
        foreach (var id in everyRule.Select(r => r.Id).Distinct())
        {
            var path = everyRule.FirstOrDefault(r => r.Id == id && r.Path.Length > 0)?.Path ?? string.Empty;
            var all = Ask.Of(setup.Apps.FirstOrDefault(r => r.Id == id));
            var each = live.Select(l => (l.Adapter, l.Category, Ask: Ask.Of(l.Rules.FirstOrDefault(r => r.Id == id)))).ToList();
            // One place for everything when every connected adapter asks the same (or none is connected).
            if (each.Count == 0 || each.All(e => e.Ask == each[0].Ask))
            {
                if (Rule(id, path, each.Count == 0 ? all : each[0].Ask) is { } everywhere) rules.Add(everywhere);
                continue;
            }
            var sameOnAdapters = each.All(e => e.Ask.OnAdapter == each[0].Ask.OnAdapter);
            var sameUpload = each.All(e => e.Ask.Up == each[0].Ask.Up);
            var common = (sameOnAdapters ? each[0].Ask : Ask.None) with { Up = sameUpload ? each[0].Ask.Up : null };
            if (Rule(id, path, common) is { } shared) rules.Add(shared);
            if (!sameOnAdapters)
                foreach (var (adapter, _, ask) in each)
                    if (Rule(id, path, ask with { Up = null }) is { } own) rules.Add(own with { Adapter = adapter.Id, AdapterName = adapter.Name });
            if (sameUpload) continue;
            foreach (var group in each.GroupBy(e => e.Category))
            {
                var ups = group.Select(e => e.Ask.Up).Distinct().ToList();
                inexact |= ups.Count > 1;
                if ((ups.Count == 1 ? ups[0] : all.Up) is { } bits) rules.Add(new NetworkRule(id, path, bits, false, Profile: group.Key));
            }
        }
        return (rules, inexact);
    }

    private static NetworkRule? Rule(string id, string path, Ask ask) => ask == Ask.None ? null
        : new NetworkRule(id, path, ask.Up, ask.Block, ask.Down, ask.UpBlock, ask.DownBlock);

    /// What one program's rule asks of Windows: an upload cap in bits per second, a download limit in bytes per second, the blocks.
    private readonly record struct Ask(ulong? Up, bool Block, long? Down, bool UpBlock, bool DownBlock)
    {
        public static readonly Ask None = default;

        /// The part that follows an adapter exactly (everything but the upload cap).
        public (bool, long?, bool, bool) OnAdapter => (Block, Down, UpBlock, DownBlock);

        public static Ask Of(AppNetRule? r) => r is null ? None : new(
            r.UploadOn && r.UploadKbps > 0 && !r.UploadBlocked ? (ulong)r.UploadKbps * 1024 * 8 : null, r.Blocked,
            r.DownloadOn && r.DownloadKbps > 0 && !r.DownloadBlocked ? r.DownloadKbps * 1024L : null, r.UploadBlocked, r.DownloadBlocked);
    }
}
