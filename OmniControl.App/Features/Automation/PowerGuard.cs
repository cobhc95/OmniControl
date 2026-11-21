using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Automation;

/// The one place that decides whether sleep, hibernate, shut down, restart or sign out may happen now, however they were started (a
/// rule, a hotkey, a mouse button, another PC): never while another one waits, and only after a countdown the user can cancel
/// (<see cref="Timing.PowerCountdown"/>), here or through <see cref="Cancel"/>. A rule that a trigger started (not a person, see <see cref="RunChain.ByPerson"/>) may not do
/// one within <see cref="Timing.PowerActionGrace"/> of OmniControl starting or the PC waking, so a start-up or wake rule can never put
/// the PC down over and over. The time is measured on the monotonic tick count, so a change of the clock never stretches the grace.
/// Each start, run-out and cancel of the countdown is said through <c>counting</c> (paired PCs show it).
public sealed class PowerGuard
{
    private const string AfterStart = "OmniControl started", AfterWake = "the PC woke";

    private readonly Func<string, string, TimeSpan, CancellationToken, Task<bool>> _countdown;
    private readonly Action<string> _report;
    private readonly Func<RunChain?> _chainNow;
    private readonly Action<PowerCountdown?> _counting;
    private long _settledFrom = Environment.TickCount64; // OmniControl's start, then each confirmed wake
    private string _since = AfterStart;
    private CancellationTokenSource? _waiting;

    /// <param name="countdown">Shows the cancellable countdown (title, text, time; the token closes it as cancelled); true when it ran out or the user said "now", false when cancelled.</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    /// <param name="chainNow">The chain of rules whose run asks now (null: a person or Windows), read when a request arrives.</param>
    /// <param name="counting">Told when a countdown starts, runs out (zero left) or is cancelled or fails (null).</param>
    public PowerGuard(Func<string, string, TimeSpan, CancellationToken, Task<bool>> countdown, Action<string> report, Func<RunChain?> chainNow, Action<PowerCountdown?> counting)
    {
        _countdown = countdown;
        _report = report;
        _chainNow = chainNow;
        _counting = counting;
    }

    /// The PC really woke (the power journal confirmed it; UI thread): rules may not put it down again for the grace.
    public void Woke()
    {
        _settledFrom = Environment.TickCount64;
        _since = AfterWake;
    }

    /// Cancels the countdown showing now, as its Cancel button does; nothing when none shows.
    public void Cancel() => _waiting?.Cancel();

    /// <param name="id">The action's id, as paired PCs know it.</param>
    /// <param name="verb">What is about to happen, as the title says it ("Shut down").</param>
    /// <param name="doIt">Does it (on a worker: sleep returns only after the wake); false when Windows refused.</param>
    /// <param name="countdown">How long the countdown lasts (null: <see cref="Timing.PowerCountdown"/>), held within the limits in <see cref="Timing"/>.</param>
    public async Task RequestAsync(string id, string verb, Func<bool> doIt, TimeSpan? countdown = null)
    {
        // Read before the first await: the chain is the one of the run that asks now.
        if (_chainNow() is { ByPerson: false } && Environment.TickCount64 - _settledFrom < (long)Timing.PowerActionGrace.TotalMilliseconds)
        {
            _report($"{verb} was not done: a rule can't do that within {Timing.PowerActionGrace.TotalMinutes:0} minutes after {_since}.");
            return;
        }
        if (_waiting is not null)
        {
            _report($"{verb} was not started: a power countdown is already showing.");
            return;
        }
        using var waiting = _waiting = new CancellationTokenSource();
        var done = false;
        try
        {
            var wait = Timing.PowerCountdownOf(countdown?.TotalSeconds);
            _counting(new PowerCountdown(id, wait));
            var go = await _countdown($"{verb}?", $"{verb} in {wait.TotalSeconds:0} seconds, unless you cancel.", wait, waiting.Token);
            if (!go) _report($"{verb} was cancelled.");
            else
            {
                _counting(new PowerCountdown(id, TimeSpan.Zero));
                done = await Task.Run(doIt);
                if (!done) _report($"Windows did not let OmniControl do that ({verb.ToLowerInvariant()}).");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nobody awaits this task: a failure is logged and said here, or it would vanish.
            Guard.Log($"Power action {verb}", ex);
            _report(Guard.SeeLog($"{verb} didn't work."));
        }
        finally
        {
            _waiting = null;
            // Done stays said: the PC is going (a paired PC sees it leave); anything else ends the countdown there too.
            if (!done) _counting(null);
        }
    }
}
