using OmniControl.App.Services;

namespace OmniControl.App.Tray;

/// <summary>When a state may be read again (pure, tested): never twice within the shortest gap, so a quick re-open never piles reads up.</summary>
public static class RecheckThrottle
{
    public static bool Due(DateTime? lastAsked, DateTime now, TimeSpan minGap) => lastAsked is not { } last || now - last >= minGap;
}

// Live states in the panel (docs/mockups/tray-tile-states.html): when it opens, and after it has drawn, each tile whose state can be read
// again cheaply (a smart device with a Scripts method that reads when the tray opens: StateEntry.Recheck) is read in the background, ONE AT A
// TIME with Timing.TrayRecheckStagger between, and never more often than Timing.TrayRecheckMinGap. What the read is doing (the grey dot, a
// failure) comes from the state's own liveness like every other mark; the answer lights or dims the tile as any change does. A state that
// cannot be read that way (a device run by simulation only) shows its last known value, faded; simulation is never woken here.
public sealed partial class TrayFlyoutModel
{
    private readonly Dictionary<string, DateTime> _lastAsked = new();
    private CancellationTokenSource? _rechecking;

    private void StartRechecks()
    {
        StopRechecks();
        _rechecking = new CancellationTokenSource();
        // Taken now: the panel may close (and dispose the source) before the deferred start runs.
        var stop = _rechecking.Token;
        DrawFirst.ThenWire(() => _ = RecheckAllAsync(stop));
    }

    private void StopRechecks()
    {
        _rechecking?.Cancel();
        _rechecking?.Dispose();
        _rechecking = null;
    }

    private async Task RecheckAllAsync(CancellationToken stop)
    {
        var first = true;
        foreach (var tile in Tiles.ToList())
        {
            if (stop.IsCancellationRequested || !_shown) return;
            if (tile.Recheck is not { } recheck || tile.Target.State is not { } state || !recheck.CanCheck()) continue;
            var now = DateTime.UtcNow;
            if (!RecheckThrottle.Due(_lastAsked.TryGetValue(state.Id, out var last) ? last : null, now, Timing.TrayRecheckMinGap)) continue;
            _lastAsked[state.Id] = now;
            try
            {
                if (!first) await Task.Delay(Timing.TrayRecheckStagger, stop);
                first = false;
                await recheck.CheckAsync();
            }
            catch (OperationCanceledException)
            {
                return; // the panel closed: the rest are not asked
            }
        }
    }
}
