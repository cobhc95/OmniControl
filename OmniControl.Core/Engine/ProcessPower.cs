using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>
/// Keeps this process quick to answer while it sits idle in the tray: Windows 11 otherwise throttles it (efficiency mode slows its
/// threads and delays its timers), gives it no head start over busy apps, and pages its memory out, all of which made a pop-up late.
/// </summary>
public static class ProcessPower
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Throttling { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int kind, ref Throttling info, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSizeEx(IntPtr process, nint minimum, nint maximum, uint flags);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);

    private const int PowerThrottling = 4;
    private const uint CurrentVersion = 1, ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;
    private const uint TimerResolutionMs = 1;
    private const uint HardMinimum = 0x1, NoHardMaximum = 0x8;

    /// <summary>
    /// Call first thing on the UI thread. Opts out of power throttling, asks for fine-grained timers, raises the process and the
    /// calling (UI) thread above normal priority, and keeps the garbage collector from pausing everything for a full collection.
    /// Safe on any Windows version.
    /// </summary>
    public static void StayResponsive()
    {
        // A zero state with the control bits set means "never throttle this"; older Windows versions simply refuse, which is fine.
        var info = new Throttling { Version = CurrentVersion, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
        SetProcessInformation(GetCurrentProcess(), PowerThrottling, ref info, Marshal.SizeOf<Throttling>());
        timeBeginPeriod(TimerResolutionMs);

        using (var self = Process.GetCurrentProcess()) self.PriorityClass = ProcessPriorityClass.AboveNormal;
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
    }

    /// <summary>
    /// Call once start-up has run the pop-up and hotkey code. Keeps at least the memory in use now resident, so Windows does not page
    /// it out while the app is idle or minimised (paging it back in was a delay before the next pop-up). The app can still grow beyond it.
    /// </summary>
    public static void KeepResident()
    {
        using var self = Process.GetCurrentProcess();
        var inUse = (nint)self.WorkingSet64;
        // Refused when the system cannot spare that much memory; Windows then manages the memory as before, which is safe.
        SetProcessWorkingSetSizeEx(self.Handle, inUse, inUse * 2, HardMinimum | NoHardMaximum);
    }
}
