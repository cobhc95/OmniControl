using OmniControl.App.Services;

namespace OmniControl.App.Automation;

/// <summary>
/// The one place that decides whether sleep, hibernate, shut down, restart or sign out may happen now, however they were started (a
/// rule, a hotkey, a mouse button): never within <see cref="Timing.PowerActionGrace"/> of OmniControl starting, never while another one
/// waits, and only after a countdown the user can cancel (<see cref="Timing.PowerCountdown"/>).
/// </summary>
public sealed class PowerGuard
{
    private readonly Func<string, string, TimeSpan, Task<bool>> _countdown;
    private readonly Action<string> _report;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private bool _waiting;

    /// <param name="countdown">Shows the cancellable countdown (title, text, time); true when it ran out or the user said "now", false when cancelled.</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    public PowerGuard(Func<string, string, TimeSpan, Task<bool>> countdown, Action<string> report)
    {
        _countdown = countdown;
        _report = report;
    }

    /// <param name="verb">What is about to happen, as the title says it ("Shut down").</param>
    /// <param name="doIt">Does it; false when Windows refused.</param>
    /// <param name="countdown">How long the countdown lasts (null: <see cref="Timing.PowerCountdown"/>), held within the limits in <see cref="Timing"/>.</param>
    public async Task RequestAsync(string verb, Func<bool> doIt, TimeSpan? countdown = null)
    {
        if (DateTime.UtcNow - _startedAt < Timing.PowerActionGrace)
        {
            _report($"{verb} was not done: OmniControl has only just started. Try again in a moment.");
            return;
        }
        if (_waiting) return;
        _waiting = true;
        try
        {
            var wait = countdown is { } asked
                ? TimeSpan.FromSeconds(Math.Clamp(asked.TotalSeconds, Timing.PowerCountdownMinSeconds, Timing.PowerCountdownMaxSeconds))
                : Timing.PowerCountdown;
            var go = await _countdown($"{verb}?", $"{verb} in {wait.TotalSeconds:0} seconds, unless you cancel.", wait);
            if (!go) _report($"{verb} was cancelled.");
            else if (!doIt()) _report($"Windows did not let OmniControl do that ({verb.ToLowerInvariant()}).");
        }
        finally
        {
            _waiting = false;
        }
    }
}
