namespace OmniControl.Core.Engine;

/// Stops Windows putting the PC to sleep on its own while on (the screens may still turn off). SetThreadExecutionState holds for the
/// thread that calls it, so one dedicated thread makes every call and, when it ends, Windows forgets the request: nothing stays held after exit.
public sealed class KeepAwake : IDisposable
{
    private const uint Continuous = 0x80000000, SystemRequired = 0x00000001;

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);

    private readonly SemaphoreSlim _signal = new(0);
    private readonly Thread _thread;
    private volatile bool _on, _stop;

    public KeepAwake()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Keep awake" };
        _thread.Start();
    }

    public bool IsOn => _on;

    public void Set(bool on)
    {
        _on = on;
        _signal.Release();
    }

    private void Run()
    {
        while (true)
        {
            _signal.Wait();
            if (_stop) break;
            SetThreadExecutionState(_on ? Continuous | SystemRequired : Continuous);
        }
        SetThreadExecutionState(Continuous);
    }

    public void Dispose()
    {
        _stop = true;
        _signal.Release();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
