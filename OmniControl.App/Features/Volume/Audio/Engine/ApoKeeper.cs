namespace OmniControl.App.Features.Volume.Audio.Engine;

/// Keeps OmniControl's effects in Equalizer APO's config while they should run (UI thread). It acts on what <see cref="ApoWatch"/> really
/// found on disk: with "Keep OmniControl's effects in place" on, a folder whose include line or file was taken out or changed (Equalizer
/// APO's own editor writes back the config it loaded) is put back at once, at most once per <see cref="Timing.ApoRestoreGap"/> (a later
/// put-back waits for the gap, so none is lost); <see cref="Timing.ApoRestoreLoopCount"/> put-backs within
/// <see cref="Timing.ApoRestoreLoopWindow"/> are said once (another program keeps taking them out). With the setting off the folder is
/// marked displaced and left alone, so its rows say so honestly until <see cref="PutBack"/>. After every burst of writes (an EQ drag, a
/// put-back) it touches OmniControl's file once more after <see cref="Timing.ApoReloadSettle"/>, so Equalizer APO loads the final chain
/// even if it was reading during the burst. A folder that should be clean is cleaned by the same put-back, never filled.
internal sealed class ApoKeeper : IDisposable
{
    private readonly ApoChains _chains;
    private readonly Func<bool> _keep;
    private readonly Action<string> _report;
    private readonly Action _changed;
    private readonly DispatcherTimer _settle = new() { Interval = Timing.ApoReloadSettle };
    private readonly DispatcherTimer _restoreWait = new();
    private readonly HashSet<string> _toNudge = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _restores = new();
    private bool _loopSaid;

    /// <param name="keep">Whether "Keep OmniControl's effects in place" is on now.</param>
    /// <param name="report">The footer line (a put-back loop is said there once).</param>
    /// <param name="changed">What the rows should say changed (a folder displaced or put back, the config's own effects): statuses again.</param>
    public ApoKeeper(ApoChains chains, Func<bool> keep, Action<string> report, Action changed)
    {
        _chains = chains;
        _keep = keep;
        _report = report;
        _changed = changed;
        _settle.Tick += OnSettled;
        _restoreWait.Tick += OnRestoreDue;
        chains.Wrote += OnWrote;
    }

    /// config.txt also runs effects of its own where OmniControl's chain runs (the last look).
    public bool OwnEffects { get; private set; }

    /// One look at the disk (<see cref="ApoWatch"/>). False when it was compared with a target that has changed since: the caller looks
    /// again rather than act on it.
    public bool Take(ApoFindings found)
    {
        if (!ReferenceEquals(found.Target, _chains.Target)) return false;
        var changed = OwnEffects != found.OwnEffects;
        OwnEffects = found.OwnEffects;
        foreach (var folder in found.InPlace) changed |= _chains.SetDisplaced(folder, false);
        if (found.OutOfPlace.Count > 0)
        {
            if (_chains.Failing) _chains.RetryIfDue(); // a write that failed is retried on its own gap, never counted as another program's doing
            else if (_keep()) RequestRestore();
            else
                foreach (var folder in found.OutOfPlace) changed |= _chains.SetDisplaced(folder, true);
        }
        if (changed) _changed();
        return true;
    }

    /// "Put back" (or the setting switched on): every folder gets OmniControl's part again at once.
    public void PutBack()
    {
        _restoreWait.Stop();
        _chains.PutBack();
        _changed();
    }

    private void RequestRestore()
    {
        if (_restoreWait.IsEnabled) return; // one is already waiting for its gap
        var since = _restores.Count > 0 ? DateTime.UtcNow - _restores.Last() : TimeSpan.MaxValue;
        if (since >= Timing.ApoRestoreGap)
        {
            Restore();
            return;
        }
        _restoreWait.Interval = Timing.ApoRestoreGap - since;
        _restoreWait.Start();
    }

    private void OnRestoreDue(object? sender, EventArgs e) => Guard.Run("Effects: putting OmniControl's effects back in Equalizer APO", Restore);

    private void Restore()
    {
        _restoreWait.Stop();
        var now = DateTime.UtcNow;
        while (_restores.Count > 0 && now - _restores.Peek() > Timing.ApoRestoreLoopWindow) _restores.Dequeue();
        if (_restores.Count == 0) _loopSaid = false; // calm again: a new loop is said again
        _restores.Enqueue(now);
        if (_restores.Count >= Timing.ApoRestoreLoopCount && !_loopSaid)
        {
            _loopSaid = true;
            _report("Another program keeps taking OmniControl's effects out of Equalizer APO's config; OmniControl puts them back each time.");
        }
        _chains.PutBack();
        _changed();
    }

    private void OnWrote(IReadOnlyCollection<string> folders)
    {
        _toNudge.UnionWith(folders);
        _settle.Stop();
        _settle.Start();
    }

    private void OnSettled(object? sender, EventArgs e) => Guard.Run("Effects: asking Equalizer APO to reload", () =>
    {
        _settle.Stop();
        var folders = _toNudge.ToList();
        _toNudge.Clear();
        _chains.Nudge(folders);
    });

    public void Dispose()
    {
        _settle.Stop();
        _restoreWait.Stop();
        _settle.Tick -= OnSettled;
        _restoreWait.Tick -= OnRestoreDue;
        _chains.Wrote -= OnWrote;
    }
}
