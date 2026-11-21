using System.Runtime;
using SkiaSharp;

namespace OmniControl.Core.Engine;

/// Keeps this process quick to answer while it sits idle in the tray without costing the PC anything while idle: Windows 11 otherwise
/// throttles it (efficiency mode slows its threads and delays its timers), which made a pop-up late. The process keeps normal priority;
/// the UI thread is above normal and the hook and hotkey threads are highest (they set that themselves). Fine timers are asked for only
/// while something on screen needs them (<see cref="TimerResolution"/>). Also gives memory back and says how much the process holds.
public static class ProcessPower
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Throttling { public uint Version, ControlMask, StateMask; }

    // PROCESS_MEMORY_COUNTERS_EX2; an older Windows fills only the fields it knows and leaves PrivateWorkingSet at zero.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSet, WorkingSet, QuotaPeakPagedPool, QuotaPagedPool, QuotaPeakNonPagedPool, QuotaNonPagedPool,
            PagefileUsage, PeakPagefileUsage, PrivateUsage, PrivateWorkingSet;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int kind, ref Throttling info, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);

    private const int PowerThrottling = 4;
    private const uint CurrentVersion = 1, ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;

    // Skia's image and blur-mask cache (32 MB by default) sized for windows this small; its glyph cache keeps Skia's own 2 MB.
    private const long DrawingCacheBytes = 8 * 1024 * 1024;

    /// Call first thing on the UI thread. Opts out of power throttling and raises the calling (UI) thread above normal. Safe on any Windows version.
    public static void StayResponsive()
    {
        // A zero state with the control bits set means "never throttle this"; older Windows versions simply refuse, which is fine.
        var info = new Throttling { Version = CurrentVersion, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
        SetProcessInformation(GetCurrentProcess(), PowerThrottling, ref info, Marshal.SizeOf<Throttling>());
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
    }

    /// Call before the first window is drawn: Skia's caches live outside the managed heap, so no collection gives them back.
    public static void LimitDrawingCaches() => SKGraphics.SetResourceCacheTotalByteLimit(DrawingCacheBytes);

    /// What Task Manager shows as this process's memory (its private working set); null where Windows does not report it.
    public static long? PrivateWorkingSet()
    {
        var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
        return GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size) && counters.PrivateWorkingSet > 0 ? (long)counters.PrivateWorkingSet : null;
    }

    /// Gives the memory the process no longer uses back to Windows: one full background collection (the runtime's ConserveMemory setting
    /// decides about compacting). For quiet moments (start-up has settled, the window was hidden a while). Never blocking: a blocking
    /// collection stops every thread, the mouse hook's and the audio thread's too, so the pointer and the sound would stall.
    public static void Compact() => GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);

    /// After big parts were let go (the windows after a long hide): Skia's caches are emptied, one full background collection runs, and
    /// the large objects are marked to be compacted at the runtime's own next full blocking collection. Never a forced blocking compaction:
    /// it stops every thread, so a hotkey, the mouse hook or the sound would pause. Working sets are never trimmed.
    public static void CompactAfterRelease()
    {
        SKGraphics.PurgeAllCaches();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        Compact();
    }
}
