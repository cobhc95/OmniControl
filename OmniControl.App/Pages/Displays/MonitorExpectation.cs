namespace OmniControl.App.Pages.Displays;

/// <summary>
/// How many DDC/CI monitors the program should find: the most it has found while the same desktop monitors were attached. Lets the monitors'
/// hardware watch say "stale" when only some came back (two monitors, one still not answering after Remote Desktop), not only when none did.
/// One per page; thread-safe (the watch is asked from timer threads).
/// </summary>
internal sealed class MonitorExpectation
{
    private readonly Dictionary<string, int> _best = new();
    private readonly object _gate = new();

    /// <summary>True when <paramref name="found"/> monitors is fewer than expected for these desktop monitors (device paths of the non-internal displays).</summary>
    public bool LooksStale(IEnumerable<string> externalDisplays, int found)
    {
        var paths = externalDisplays.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var key = string.Join('|', paths);
        lock (_gate)
        {
            var best = _best.GetValueOrDefault(key);
            if (found > best) _best[key] = found;
            return found < best || (found == 0 && paths.Count > 0);
        }
    }
}
