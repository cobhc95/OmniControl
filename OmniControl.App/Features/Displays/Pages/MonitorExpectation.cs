namespace OmniControl.App.Features.Displays.Pages;

/// What the program should find on the monitors, learned while it runs: the most DDC/CI monitors found while the same desktop monitors were
/// attached, and the most codes each monitor has answered. Lets the monitors' hardware watch say "stale" when only some came back (two
/// monitors, one still not answering after Remote Desktop) or a monitor answers less than it did (read while it was waking), not only when
/// none did. One per page; thread-safe (the watch is asked from timer threads).
internal sealed class MonitorExpectation
{
    private readonly Dictionary<string, int> _best = new();
    private readonly Dictionary<string, int> _bestCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// True when <paramref name="found"/> monitors is fewer than expected for these desktop monitors (device paths of the non-internal displays).
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

    /// True when <paramref name="monitor"/> (its device path) answered more than <paramref name="codes"/> codes before in this run.
    public bool AnswersLess(string monitor, int codes)
    {
        lock (_gate)
        {
            var best = _bestCodes.GetValueOrDefault(monitor);
            if (codes > best) _bestCodes[monitor] = codes;
            return codes < best;
        }
    }
}
