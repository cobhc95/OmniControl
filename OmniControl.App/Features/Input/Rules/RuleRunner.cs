using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Rules;

/// Carries out a rule's steps in order, off the hook thread: waits, commands (<see cref="ScriptRunner"/>), and every other action
/// through a <see cref="MouseActionRunner"/> (with its launch limiter). A command or program step that the user has not allowed (its
/// hash is not in the approvals) is never run, and the rest of the rule is skipped. Keys are only ever sent as a whole press and
/// release, so nothing is left held, also when a step fails. A step whose action works over the network (<see cref="ActionReach"/> on its
/// master entry) is, when the caller passes a <see cref="RunReport"/>, waited for (network up, device answers) if the report asks, then run
/// and followed to its end, and tried again with backoff if it failed (<see cref="ReadyWait"/>); the report gets the tries and a short reason.
public sealed class RuleRunner
{
    private readonly MouseActionRunner _actions;
    private readonly Func<string, bool> _isApproved;
    private readonly Action<string> _log;
    private readonly Func<string, ActionReach?> _reachOf;
    private readonly Func<bool> _networkUp;

    /// <param name="actions">Runs the plain actions.</param>
    /// <param name="isApproved">True when the user allowed this stored command or program step.</param>
    /// <param name="log">Writes a line to the rules log.</param>
    /// <param name="reachOf">The <see cref="ActionReach"/> of a master action by id (null: none, the step just runs).</param>
    /// <param name="networkUp">Whether the network is up now; <see cref="NetworkState.IsUp"/> unless a test sets it.</param>
    public RuleRunner(MouseActionRunner actions, Func<string, bool> isApproved, Action<string> log, Func<string, ActionReach?>? reachOf = null,
        Func<bool>? networkUp = null)
    {
        _actions = actions;
        _isApproved = isApproved;
        _log = log;
        _reachOf = reachOf ?? (_ => null);
        _networkUp = networkUp ?? NetworkState.IsUp;
    }

    /// Runs the steps; returns null when all went well, otherwise one line saying what went wrong (also logged).
    /// <param name="at">Where the trigger happened (window actions act on the window there).</param>
    /// <param name="waitsBefore">Seconds to wait before each step (parallel to <paramref name="steps"/>); null = none.</param>
    /// <param name="stop">Ends the run between steps and during a wait (the automations' "pause all rules"); a stopped run is not a failure.</param>
    /// <param name="report">Asks for the readiness wait and retries of steps that need the network or a device, and gets their result; null: they just run.</param>
    public async Task<string?> RunAsync(string name, IReadOnlyList<string> steps, MouseEvent at, Func<int, int>? waitsBefore = null,
        CancellationToken stop = default, RunReport? report = null)
    {
        _log($"rule \"{name}\" runs");
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                if (waitsBefore?.Invoke(i) is > 0 and var seconds) await Task.Delay(TimeSpan.FromSeconds(seconds), stop);
                stop.ThrowIfCancellationRequested();
                if (await RunStepAsync(steps[i], at, report, stop) is not { } problem) continue;
                _log($"rule \"{name}\": {problem}");
                return problem;
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            _log($"rule \"{name}\" stopped: rules were paused");
            return null;
        }
        catch (Exception ex)
        {
            // A step calls into Windows and other programs; whatever goes wrong ends only this run, is logged and shown on the rule,
            // and must never take OmniControl down (this runs on a worker thread with nothing above it to catch it).
            _log($"rule \"{name}\" failed: {ex}");
            return $"failed: {ex.Message}";
        }
    }

    private async Task<string?> RunStepAsync(string stored, MouseEvent at, RunReport? report, CancellationToken stop)
    {
        if (RuleSteps.NeedsApproval(stored) && !_isApproved(stored))
            return $"skipped \"{RuleSteps.Describe(stored)}\": it has not been allowed yet (open the rule and choose it again to allow it)";

        var step = MouseAction.Parse(stored);
        switch (step.Id)
        {
            case RuleSteps.Wait:
                await Task.Delay(step.Options.Int(RuleSteps.WaitTime));
                return null;
            case RuleSteps.Command:
                var shell = step.Options.Text(RuleSteps.Shell);
                var command = step.Options.Text(RuleSteps.CommandText);
                if (command.Trim().Length == 0) return "its command is empty";
                _log($"running {ScriptRunner.Describe(shell, command)}");
                var problem = await ScriptRunner.RunAsync(shell, command, step.Options.Bool(RuleSteps.Hidden),
                    TimeSpan.FromSeconds(step.Options.Int(RuleSteps.Timeout)));
                return problem is null ? null : $"the command {problem}";
            case MouseActionCatalog.Omni when report is not null && _reachOf(step.Param) is { } reach:
                return await RunReachingAsync(step, at, reach, report, stop);
            default:
                _actions.Run(step, at);
                return null;
        }
    }

    /// A step that works over the network: waited for when asked, run, followed to its end, tried again on failure.
    private async Task<string?> RunReachingAsync(MouseAction step, MouseEvent at, ActionReach reach, RunReport report, CancellationToken stop)
    {
        if (report.WaitForReady)
        {
            report.Waiting?.Invoke(reach.Target);
            ReadyResult ready;
            try { ready = await ReadyWait.UntilAsync(ct => ProbeAsync(reach, ct), Timing.ReadyBackoff, Timing.ReadyTimeout, Task.Delay, stop); }
            finally { report.Waiting?.Invoke(null); }
            _log(ready.Ready
                ? $"{reach.Target} ready after {ready.Checks} check{(ready.Checks == 1 ? string.Empty : "s")}"
                : $"{reach.Target} not ready after {ready.Checks} checks: {ready.Problem}");
            if (!ready.Ready)
            {
                report.Short = $"{reach.Target} not reachable";
                return $"{reach.Target} was not reachable ({ready.Problem?.TrimEnd('.')})";
            }
        }
        var (outcome, tries) = await ReadyWait.RetryAsync(async tryNumber =>
        {
            if (tryNumber > 1 && await ProbeAsync(reach, stop) is { } notReady) return new StepOutcome($"{reach.Target} not reachable: {notReady.TrimEnd('.')}");
            var since = DateTime.Now;
            _actions.Run(step, at);
            var result = await reach.OutcomeSince(since, stop);
            _log($"{reach.Target}, try {tryNumber}: {result.Problem ?? "done"}");
            return result;
        }, Timing.StepRetryBackoff, Timing.StepTriesMax, Task.Delay, stop);
        report.Tries = Math.Max(report.Tries, tries);
        if (outcome.Problem is null) return null;
        report.Short ??= $"{reach.Target} failed";
        return $"{outcome.Problem.TrimEnd('.')} (after {tries} tr{(tries == 1 ? "y" : "ies")})";
    }

    /// Null when the network (if needed) is up and the thing answers; otherwise why not.
    private async Task<string?> ProbeAsync(ActionReach reach, CancellationToken stop)
    {
        if (reach.NeedsNetwork && !_networkUp()) return "the network is not up yet";
        return reach.Probe is null ? null : await reach.Probe(stop);
    }
}
