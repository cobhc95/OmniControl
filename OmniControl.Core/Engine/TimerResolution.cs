namespace OmniControl.Core.Engine;

/// Fine-grained (1 ms) Windows timers, asked for only while something needs them (a level meter, a live graph, a recording, a pop-up
/// fading): each <see cref="Hold"/> asks Windows once and gives it back once when disposed. Windows counts the requests itself, so
/// holders never need to know of each other; while nobody holds one, the PC keeps its normal, power-saving timer.
public static class TimerResolution
{
    private const uint PeriodMs = 1;

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

    public static IDisposable Hold() => new Held();

    private sealed class Held : IDisposable
    {
        private int _released;

        public Held() => timeBeginPeriod(PeriodMs);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) timeEndPeriod(PeriodMs);
        }
    }
}
