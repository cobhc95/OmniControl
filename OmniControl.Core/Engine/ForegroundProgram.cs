namespace OmniControl.Core.Engine;

/// <summary>
/// The program the user is working in, kept ready by a worker (every <see cref="CoreTiming.ForegroundWatchMs"/>) so the mouse hook never
/// looks a program up itself. The worker starts with the first question and is a background thread, so it ends with the program.
/// </summary>
public sealed class ForegroundProgram
{
    private readonly ProgramIdentity _identity = new();
    private readonly object _gate = new();
    private volatile string _name = string.Empty;
    private bool _started;

    /// <summary>The foreground program's identity (<see cref="ProgramIdentity.Normalize"/>), or empty; at most one watch interval old.</summary>
    public string Name
    {
        get
        {
            Start();
            return _name;
        }
    }

    private void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _name = _identity.Foreground(); // the very first answer needs no wait
            new Thread(Watch) { IsBackground = true, Name = "ForegroundProgram", Priority = ThreadPriority.BelowNormal }.Start();
        }
    }

    private void Watch()
    {
        while (true)
        {
            Thread.Sleep(CoreTiming.ForegroundWatchMs);
            _name = _identity.Foreground();
        }
    }
}
