namespace OmniControl.App.Rules;

/// <summary>
/// What a rule's caller wants from one run of <see cref="RuleRunner"/> and learns back, for the steps that need the network or a device
/// (<see cref="Hotkeys.ActionReach"/>): whether to wait for them to answer first (after a wake or a log-in), who is told while it waits, and
/// how many tries the steps needed and the short reason a step failed (for the rule's tag). Used by one run on one worker.
/// </summary>
public sealed class RunReport
{
    /// <summary>Wait until the network is up and the device answers before running such a step.</summary>
    public bool WaitForReady { get; init; }

    /// <summary>Told the name of what the run is waiting for, then null when the wait is over (any thread).</summary>
    public Action<string?>? Waiting { get; init; }

    /// <summary>The most tries any such step needed (1 when every one worked first time).</summary>
    public int Tries { get; set; } = 1;

    /// <summary>A failed step's reason in a few words ("Orbit 11 not reachable"), or null.</summary>
    public string? Short { get; set; }
}
