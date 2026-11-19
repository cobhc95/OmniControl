namespace OmniControl.App.Features.Devices;

/// What is known about a device since OmniControl started (not saved): whether it is busy or being set up, its last known state and how the last command went.
public sealed class DeviceRuntime
{
    /// A command is running.
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

    /// The running command is only a read of the state (Check now, or an auto-check): the tag says "checking…".
    public bool Checking { get; set; }

    /// The device's set-up is open on the page: its emulator starts with its window. Everything else runs it hidden.
    public bool InSetup { get; set; }

    /// The device's last known state: for a simulated device the state read on its app's screen, for a scripted plug the state it was last
    /// told to take (a switch by hand is not seen); null when it is not known.
    public bool? IsOn { get; private set; }

    /// When <see cref="IsOn"/> was read or set; null until then.
    public DateTime? KnownAt { get; private set; }

    /// The last command's result was read back from the device (the simulation's reading, or a template's State command), so it is certain.
    public bool Confirmed { get; private set; }

    /// The last run could not read the state (so On and Off tapped nothing); <see cref="FailedMethodId"/> is the method that could not.
    public bool Unsure { get; private set; }

    /// "Last command 08:02 · turned off in 0.6 s", or what went wrong; empty until one ran.
    public string LastLine { get; private set; } = string.Empty;

    public bool LastFailed { get; private set; }

    /// The last command's log lines with the password and email masked (what "Copy details" copies); empty when there are none.
    public string Details { get; private set; } = string.Empty;

    /// The simulation method: the step the last run stopped at (0 = the first), or -1; with the short phrase saying why and the method it was in.
    public int FailedStep { get; private set; } = -1;

    public string FailedMethodId { get; private set; } = string.Empty;

    public string FailedReason { get; private set; } = string.Empty;

    /// The method the running command is trying now (empty when none); each method row's power button shows its spinner for its own.
    public string RunningMethodId { get; private set; } = string.Empty;

    /// Why a method's last try failed (by method id); a method that worked is not listed. Lets each method row's power button show its own warning.
    private readonly Dictionary<string, string> _methodProblems = new();

    public string? ProblemOf(string methodId) => _methodProblems.GetValueOrDefault(methodId);

    private readonly object _stopGate = new();
    private CancellationTokenSource? _stop;

    /// A command starts: the token that <see cref="Stop"/> cancels.
    public CancellationToken BeginRun()
    {
        lock (_stopGate)
        {
            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            return _stop.Token;
        }
    }

    /// The command is over.
    public void EndRun()
    {
        lock (_stopGate)
        {
            _stop?.Dispose();
            _stop = null;
        }
    }

    /// The user stops the running command (its method's power button): the wait ends and a program we started is killed.
    public void Stop()
    {
        lock (_stopGate) _stop?.Cancel();
    }

    /// A method starts (<paramref name="methodId"/>) or the command is over (empty).
    public void Trying(string methodId)
    {
        RunningMethodId = methodId;
        Changed?.Invoke();
    }

    /// A method's try ended: its problem, or null when it worked.
    public void Tried(string methodId, string? problem)
    {
        if (problem is null) _methodProblems.Remove(methodId);
        else _methodProblems[methodId] = problem;
    }

    /// When the last On, Off or Toggle command ended (null until one did), and its problem (null when it worked); checks do not count.
    public DateTime? CommandEnded { get; private set; }

    public string? CommandProblem { get; private set; }

    /// An On, Off or Toggle command ended with this problem (null: it worked). A rule step that started it reads it (<see cref="Hotkeys.ActionReach"/>).
    public void Commanded(string? problem)
    {
        CommandProblem = problem;
        CommandEnded = DateTime.Now;
    }

    /// Raised when any of it changed (may come from another thread).
    public event Action? Changed;

    /// A command ended. <paramref name="known"/>: it read or set the state (otherwise the last known state stays).
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
