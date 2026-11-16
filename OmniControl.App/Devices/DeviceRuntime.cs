namespace OmniControl.App.Devices;

/// <summary>What is known about a device since OmniControl started (not saved): whether it is busy, the last state it was told to take and how the last command went.</summary>
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

    /// <summary>The state the plug was last told to take; null until a command worked (a switch by hand is not seen).</summary>
    public bool? IsOn { get; private set; }

    /// <summary>"Last command 08:02 · turned off in 0.6 s", or what went wrong; empty until one ran.</summary>
    public string LastLine { get; private set; } = string.Empty;

    public bool LastFailed { get; private set; }

    /// <summary>The simulation method: the step the last run stopped at (0 = the first), or -1; with the short phrase saying why.</summary>
    public int FailedStep { get; private set; } = -1;

    public string FailedReason { get; private set; } = string.Empty;

    /// <summary>Raised when any of it changed (may come from another thread).</summary>
    public event Action? Changed;

    public void Finished(bool? isOn, string line, bool failed, int failedStep = -1, string failedReason = "")
    {
        FailedStep = failedStep;
        FailedReason = failedReason;
        IsOn = isOn;
        LastLine = line;
        LastFailed = failed;
        Changed?.Invoke();
    }
}
