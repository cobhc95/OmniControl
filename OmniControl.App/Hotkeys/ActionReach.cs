namespace OmniControl.App.Hotkeys;

/// <summary>How a step that was tried went: null problem = it worked; <paramref name="MayRetry"/> false = trying again would not help (stopped by the user).</summary>
public sealed record StepOutcome(string? Problem, bool MayRetry = true)
{
    public static readonly StepOutcome Done = new((string?)null);
}

/// <summary>
/// What a master action that works on something over the network (a smart device) tells a rule that runs it, set by the action's owner on the
/// entry (<see cref="HotkeyAction.Reach"/>): what it works on, whether it needs the network, how to ask whether that thing answers now, and how
/// a run started at a given time ended (the run itself still goes through the one run path, <see cref="HotkeyRegistry"/>). The rule runner
/// (<c>Rules/ReadyWait</c>) uses it to wait for readiness after a wake or a log-in, and to try a failed step again.
/// </summary>
/// <param name="Target">The thing's name for the rule's tag and log ("Orbit 11").</param>
/// <param name="NeedsNetwork">The step cannot work before the network is up.</param>
/// <param name="Probe">Asks the thing without changing it: null when it answers now, otherwise why not. Null = no probe (the network check is all).</param>
/// <param name="OutcomeSince">Waits for the end of the run started at the given time (or sees that nothing ran) and says how it went.</param>
public sealed record ActionReach(string Target, bool NeedsNetwork, Func<CancellationToken, Task<string?>>? Probe,
    Func<DateTime, CancellationToken, Task<StepOutcome>> OutcomeSince);
