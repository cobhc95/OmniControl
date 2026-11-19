namespace OmniControl.App.Shared.Scripts;

/// What a rule's caller wants from one run of <see cref="RuleRunner"/> and learns back, for the steps that need the network or a device
/// (<see cref="Features.Hotkeys.ActionReach"/>): whether to wait for them to answer first (after a wake or a log-in), who is told while it waits, and
/// how many tries the steps needed and the short reason a step failed (for the rule's tag). Used by one run on one worker.
public sealed class RunReport
{
    /// Wait until the network is up and the device answers before running such a step.
    public bool WaitForReady { get; init; }

    /// Told the name of what the run is waiting for, then null when the wait is over (any thread).
    public Action<string?>? Waiting { get; init; }

    /// The most tries any such step needed (1 when every one worked first time).
    public int Tries { get; set; } = 1;

    /// A failed step's reason in a few words ("Orbit 11 not reachable"), or null.
    public string? Short { get; set; }
}
