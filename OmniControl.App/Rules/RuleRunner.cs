using OmniControl.App.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Rules;

/// <summary>
/// Carries out a rule's steps in order, off the hook thread: waits, commands (<see cref="ScriptRunner"/>), and every other action
/// through a <see cref="MouseActionRunner"/> (with its launch limiter). A command or program step that the user has not allowed (its
/// hash is not in the approvals) is never run, and the rest of the rule is skipped. Keys are only ever sent as a whole press and
/// release, so nothing is left held, also when a step fails.
/// </summary>
public sealed class RuleRunner
{
    private readonly MouseActionRunner _actions;
    private readonly Func<string, bool> _isApproved;
    private readonly Action<string> _log;

    /// <param name="actions">Runs the plain actions.</param>
    /// <param name="isApproved">True when the user allowed this stored command or program step.</param>
    /// <param name="log">Writes a line to the rules log.</param>
    public RuleRunner(MouseActionRunner actions, Func<string, bool> isApproved, Action<string> log)
    {
        _actions = actions;
        _isApproved = isApproved;
        _log = log;
    }

    /// <summary>Runs the steps; returns null when all went well, otherwise one line saying what went wrong (also logged).</summary>
    /// <param name="at">Where the trigger happened (window actions act on the window there).</param>
    public async Task<string?> RunAsync(string name, IReadOnlyList<string> steps, MouseEvent at)
    {
        _log($"rule \"{name}\" runs");
        try
        {
            foreach (var stored in steps)
            {
                if (await RunStepAsync(stored, at) is not { } problem) continue;
                _log($"rule \"{name}\": {problem}");
                return problem;
            }
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

    private async Task<string?> RunStepAsync(string stored, MouseEvent at)
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
            default:
                _actions.Run(step, at);
                return null;
        }
    }
}
