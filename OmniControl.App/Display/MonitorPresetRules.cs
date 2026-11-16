using OmniControl.Core.Models;

namespace OmniControl.App.Display;

/// <summary>
/// One feature a connected monitor reports, as the page knows it now. A level's <paramref name="Value"/> is its 0-100 share of
/// <paramref name="Max"/> (the monitor's own top value); a choice's is the raw value, and <paramref name="Allowed"/> lists the raw values it accepts.
/// </summary>
public sealed record LiveFeature(int Code, int Value, uint Max, IReadOnlyList<uint> Allowed);

/// <summary>A connected monitor: Windows' id for it now, its model name and what it reports.</summary>
public sealed record LiveMonitor(string Id, string Name, IReadOnlyList<LiveFeature> Features);

/// <summary>One value to write to a monitor.</summary>
public sealed record PlannedWrite(int Code, VcpKind Kind, int Value);

/// <summary>
/// The rules of monitor presets, all pure (no monitor is touched): what a preset keeps of the monitors now connected, which of them a
/// saved entry belongs to, whether a preset shows what the monitors show, and what applying it would write. Only the features of
/// <see cref="VcpFeatures"/> are kept: no power or sleep, no reset action, and nothing a hand-edited file adds.
/// </summary>
public static class MonitorPresetRules
{
    private static VcpFeature? FeatureOf(int code) => VcpFeatures.All.FirstOrDefault(f => f.Code == code);

    /// <summary>One entry per monitor that reports at least one feature a preset can keep.</summary>
    public static List<MonitorEntry> Capture(IReadOnlyList<LiveMonitor> live)
    {
        var entries = new List<MonitorEntry>();
        foreach (var monitor in live)
        {
            var values = monitor.Features.Where(f => FeatureOf(f.Code) is not null)
                .Select(f => new FeatureValue { Code = f.Code, Value = f.Value }).ToList();
            if (values.Count > 0) entries.Add(new MonitorEntry { Name = monitor.Name, Id = monitor.Id, Values = values });
        }
        return entries;
    }

    /// <summary>True when there is something to save: some connected monitor reports a feature a preset keeps.</summary>
    public static bool HasControls(IReadOnlyList<LiveMonitor> live) => Capture(live).Count > 0;

    /// <summary>
    /// Which connected monitor each saved entry belongs to: the same name and id first; then a monitor whose name (its model) is
    /// the only one of that name connected and the only unclaimed entry of that name, so a monitor moved to another port still matches.
    /// </summary>
    public static List<(LiveMonitor Monitor, MonitorEntry Entry)> Pair(IReadOnlyList<MonitorEntry> entries, IReadOnlyList<LiveMonitor> live)
    {
        var pairs = new List<(LiveMonitor, MonitorEntry)>();
        var free = entries.ToList();
        var loose = new List<LiveMonitor>();
        foreach (var monitor in live)
        {
            var exact = free.FirstOrDefault(e => e.Name == monitor.Name && e.Id == monitor.Id);
            if (exact is null) { loose.Add(monitor); continue; }
            pairs.Add((monitor, exact));
            free.Remove(exact);
        }
        foreach (var monitor in loose)
        {
            var sameName = free.Where(e => e.Name == monitor.Name).ToList();
            if (sameName.Count != 1 || loose.Count(m => m.Name == monitor.Name) != 1) continue;
            pairs.Add((monitor, sameName[0]));
            free.Remove(sameName[0]);
        }
        return pairs;
    }

    /// <summary>
    /// True when every stored value of a connected monitor equals what the monitor shows now, and at least one value was compared (a
    /// preset of monitors that are all away shows nothing). Values the monitor no longer reports are not compared.
    /// </summary>
    public static bool Matches(MonitorPreset preset, IReadOnlyList<LiveMonitor> live)
    {
        var compared = 0;
        foreach (var (monitor, entry) in Pair(preset.Monitors, live))
        {
            foreach (var value in entry.Values)
            {
                if (FeatureOf(value.Code) is not { } feature || monitor.Features.FirstOrDefault(f => f.Code == value.Code) is not { } shown) continue;
                if (!Same(feature, value.Value, shown)) return false;
                compared++;
            }
        }
        return compared > 0;
    }

    /// <summary>
    /// What applying the entry writes to the monitor: every kept feature the monitor still reports whose value differs from its own.
    /// A level outside 0-100 is brought into range; a choice the monitor does not accept is skipped. Never power.
    /// </summary>
    public static List<PlannedWrite> Plan(MonitorEntry entry, LiveMonitor monitor)
    {
        var writes = new List<PlannedWrite>();
        foreach (var value in entry.Values)
        {
            if (FeatureOf(value.Code) is not { } feature || monitor.Features.FirstOrDefault(f => f.Code == value.Code) is not { } shown) continue;
            var wanted = feature.Kind == VcpKind.Level ? Percent.Clamp(value.Value) : value.Value;
            if (feature.Kind == VcpKind.Choice && (wanted < 0 || !shown.Allowed.Contains((uint)wanted))) continue;
            if (Same(feature, wanted, shown)) continue;
            writes.Add(new PlannedWrite(value.Code, feature.Kind, wanted));
        }
        return writes;
    }

    /// <summary>
    /// A level equals what the monitor shows when it is the same share, or the share the monitor reads back after being written it
    /// (a monitor whose range is not 0-100 rounds), so a preset just applied still reads as applied after the monitor is read again.
    /// </summary>
    private static bool Same(VcpFeature feature, int stored, LiveFeature shown)
    {
        if (feature.Kind == VcpKind.Choice) return stored == shown.Value;
        var clamped = Percent.Clamp(stored);
        return shown.Value == clamped || shown.Value == Percent.Of(Percent.ToRaw(clamped, shown.Max), shown.Max);
    }
}
