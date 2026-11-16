using Avalonia.Threading;
using OmniControl.App.Services;

namespace OmniControl.App.Tray;

/// <summary>When a state may be read again (pure, tested): never twice within the shortest gap, so a re-open or the timer never piles reads up.</summary>
public static class RecheckThrottle
{
    public static bool Due(DateTime? lastAsked, DateTime now, TimeSpan minGap) => lastAsked is not { } last || now - last >= minGap;
}

// Live states in the panel: when it opens, and every Timing.TrayRecheckEvery while it is open, each tile whose state can be read again
// cheaply (a smart device with a Scripts method: StateEntry.Recheck) reads it in the background, after the panel is drawn and never more
// often than Timing.TrayRecheckMinGap. A tile shows a small dot while its read runs; the answer lights or dims it as any change does. A
// state that cannot be read that way (a device run by simulation only) shows its last known value, faded; simulation is never woken here.
public sealed partial class TrayFlyoutModel
{
    private readonly DispatcherTimer _rechecks;
    private readonly Dictionary<string, DateTime> _lastAsked = new();

    private void StartRechecks()
    {
        DrawFirst.ThenWire(Recheck);
        _rechecks.Start();
    }

    private void OnRecheckTick(object? sender, EventArgs e) => Recheck();

    private void Recheck()
    {
        if (!_shown) return;
        var now = DateTime.UtcNow;
        foreach (var tile in Tiles.OfType<TrayTileItem>())
        {
            if (tile.Recheck is not { } recheck || tile.Target.State is not { } state || !recheck.CanCheck()) continue;
            if (!RecheckThrottle.Due(_lastAsked.TryGetValue(state.Id, out var last) ? last : null, now, Timing.TrayRecheckMinGap)) continue;
            _lastAsked[state.Id] = now;
            _ = RecheckAsync(tile, recheck);
        }
    }

    private static async Task RecheckAsync(TrayTileItem tile, States.StateRecheck recheck)
    {
        tile.IsChecking = true;
        try { await recheck.CheckAsync(); }
        finally { tile.IsChecking = false; }
    }
}
