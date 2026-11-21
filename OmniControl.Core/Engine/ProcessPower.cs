namespace OmniControl.Core.Engine;

/// Keeps this process quick to answer while it sits idle in the tray without costing the PC anything while idle: Windows 11 otherwise
/// throttles it (efficiency mode slows its threads and delays its timers), which made a pop-up late. The process keeps normal priority;
/// the UI thread is above normal and the hook and hotkey threads are highest (they set that themselves). Fine timers are asked for only
/// while something on screen needs them (<see cref="TimerResolution"/>).
public static class ProcessPower
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Throttling { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int kind, ref Throttling info, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

    private const int PowerThrottling = 4;
    private const uint CurrentVersion = 1, ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;

    /// Call first thing on the UI thread. Opts out of power throttling and raises the calling (UI) thread above normal. Safe on any Windows version.
    public static void StayResponsive()
    {
        // A zero state with the control bits set means "never throttle this"; older Windows versions simply refuse, which is fine.
        var info = new Throttling { Version = CurrentVersion, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
        SetProcessInformation(GetCurrentProcess(), PowerThrottling, ref info, Marshal.SizeOf<Throttling>());
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
    }

    /// Gives the memory the process no longer uses back to Windows: one full background collection (the runtime's ConserveMemory setting
    /// decides about compacting). For quiet moments (start-up has settled, the window was hidden a while). Never blocking: a blocking
    /// collection stops every thread, the mouse hook's and the audio thread's too, so the pointer and the sound would stall.
    public static void Compact() => GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
}
