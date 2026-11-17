namespace OmniControl.App.Devices;

/// <summary>What is known about a device since OmniControl started (not saved): whether it is busy or being set up, its last known state and how the last command went.</summary>
public sealed class DeviceRuntime
{
    /// <summary>A command is running.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            Changed?.Invoke();
        }
    }

    private bool _busy;

    /// <summary>The running command is only a read of the state (Check now, or an auto-check): the tag says "checking…".</summary>
    public bool Checking { get; set; }

    /// <summary>The device's set-up is open on the page: its emulator starts with its window. Everything else runs it hidden.</summary>
    public bool InSetup { get; set; }

    /// <summary>
    /// The device's last known state: for a simulated device the state read on its app's screen, for a scripted plug the state it was last
    /// told to take (a switch by hand is not seen); null when it is not known.
    /// </summary>
    public bool? IsOn { get; private set; }

    /// <summary>When <see cref="IsOn"/> was read or set; null until then.</summary>
    public DateTime? KnownAt { get; private set; }

    /// <summary>The last command's result was read back from the device (the simulation's reading, or a template's State command), so it is certain.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The last run could not read the state (so On and Off tapped nothing); <see cref="FailedMethodId"/> is the method that could not.</summary>
    public bool Unsure { get; private set; }

    /// <summary>"Last command 08:02 · turned off in 0.6 s", or what went wrong; empty until one ran.</summary>
    public string LastLine { get; private set; } = string.Empty;

    public bool LastFailed { get; private set; }

    /// <summary>The last command's log lines with the password and email masked (what "Copy details" copies); empty when there are none.</summary>
    public string Details { get; private set; } = string.Empty;

    /// <summary>The simulation method: the step the last run stopped at (0 = the first), or -1; with the short phrase saying why and the method it was in.</summary>
    public int FailedStep { get; private set; } = -1;

    public string FailedMethodId { get; private set; } = string.Empty;

    public string FailedReason { get; private set; } = string.Empty;

    /// <summary>The method the running command is trying now (empty when none); each method row's power button shows its spinner for its own.</summary>
    public string RunningMethodId { get; private set; } = string.Empty;

    /// <summary>Why a method's last try failed (by method id); a method that worked is not listed. Lets each method row's power button show its own warning.</summary>
    private readonly Dictionary<string, string> _methodProblems = new();

    public string? ProblemOf(string methodId) => _methodProblems.GetValueOrDefault(methodId);

    private readonly object _stopGate = new();
    private CancellationTokenSource? _stop;

    /// <summary>A command starts: the token that <see cref="Stop"/> cancels.</summary>
    public CancellationToken BeginRun()
    {
        lock (_stopGate)
        {
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            return _stop.Token;
        }
    }

    /// <summary>The command is over.</summary>
    public void EndRun()
    {
        lock (_stopGate)
        {
            _stop?.Dispose();
            _stop = null;
        }
    }

    /// <summary>The user stops the running command (its method's power button): the wait ends and a program we started is killed.</summary>
    public void Stop()
    {
        lock (_stopGate) _stop?.Cancel();
    }

    /// <summary>A method starts (<paramref name="methodId"/>) or the command is over (empty).</summary>
    public void Trying(string methodId)
    {
        RunningMethodId = methodId;
        Changed?.Invoke();
    }

    /// <summary>A method's try ended: its problem, or null when it worked.</summary>
    public void Tried(string methodId, string? problem)
    {
        if (problem is null) _methodProblems.Remove(methodId);
        else _methodProblems[methodId] = problem;
    }

    /// <summary>Raised when any of it changed (may come from another thread).</summary>
    public event Action? Changed;

    /// <summary>A command ended. <paramref name="known"/>: it read or set the state (otherwise the last known state stays).</summary>
    public void Finished(bool? isOn, bool known, string line, bool failed, int failedStep = -1, string failedReason = "", bool unsure = false, string details = "", bool confirmed = false,
        string failedMethodId = "")
    {
        FailedMethodId = failedMethodId;
        Details = details;
        Confirmed = confirmed;
        FailedStep = failedStep;
        FailedReason = failedReason;
        if (known)
        {
            IsOn = isOn;
            KnownAt = DateTime.Now;
        }
        Unsure = unsure;
        LastLine = line;
        LastFailed = failed;
        Changed?.Invoke();
    }
}
