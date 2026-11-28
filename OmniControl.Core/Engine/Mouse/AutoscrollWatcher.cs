namespace OmniControl.Core.Engine.Mouse;

/// Keeps the answer to "may a middle press here be swallowed?" ready before the press, so the mouse hook never waits for a UI Automation
/// lookup (Windows drops a hook that is too slow). While enabled, a worker sleeps until the hook says the pointer moved (<see cref="Moved"/>),
/// then looks under the pointer whenever it has moved to a spot the cached answer does not cover, and goes back to sleep once the pointer
/// has rested for <see cref="CoreTiming.AutoscrollRestLooks"/> looks (the answer it rests on is kept while the pointer stays put, also over
/// a locked screen, where nothing moves). <see cref="Verdict"/> only compares the pointer's window, process and position with that answer.
/// No fresh answer means pass (fail open).
public sealed class AutoscrollWatcher : IDisposable
{
    private sealed record Entry(IntPtr Window, int Process, int X, int Y, long At, ClickVerdict Verdict);

    // The time of an answer the resting pointer stays on: never older than the cache (renewed when the pointer moves again).
    private const long Resting = long.MaxValue / 2;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _moved = new(false);
    private volatile Entry? _entry;
    private bool _enabled;
    private Thread? _worker;

    /// Starts or stops the worker; stopped, the cache is dropped and every press passes.
    public void SetEnabled(bool on)
    {
        lock (_gate)
        {
            if (_enabled == on) return;
            _enabled = on;
            _moved.Set(); // a sleeping worker wakes to start, or to see it was stopped
            if (!on) { _entry = null; return; }
            if (_worker is { IsAlive: true }) return; // the old worker has not noticed it was stopped yet and carries on
            _worker = new Thread(Watch) { IsBackground = true, Name = "AutoscrollWatcher", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    /// The hook saw the pointer move (hook thread): wakes the worker. Only sets an event.
    public void Moved()
    {
        if (_enabled) _moved.Set();
    }

    public void Dispose()
    {
        SetEnabled(false);
        _worker?.Join(CoreTiming.AutoscrollWatchMs * 4);
        _moved.Dispose();
    }

    private bool Enabled { get { lock (_gate) return _enabled; } }

    /// Runs on the hook thread: reads the cache only, never calls UI Automation. Pass unless a fresh answer for this spot says swallow.
    public ClickVerdict Verdict(int x, int y)
    {
        if (_entry is not { Verdict: ClickVerdict.Swallow } entry) return ClickVerdict.Pass;
        if (!Covers(entry, x, y)) return ClickVerdict.Pass;
        // The pointer being the hand means a link whatever the cache says; cheap enough for the hook.
        return PointerShape.IsHandShown() ? ClickVerdict.Pass : ClickVerdict.Swallow;
    }

    private static bool Covers(Entry e, int x, int y) =>
        Environment.TickCount64 - e.At <= CoreTiming.AutoscrollCacheMs
        && Math.Abs(e.X - x) <= CoreTiming.AutoscrollCachePx && Math.Abs(e.Y - y) <= CoreTiming.AutoscrollCachePx
        && SameWindow(e, x, y);

    private static bool SameWindow(Entry e, int x, int y)
    {
        var root = WindowQuery.RootWindowAt(x, y);
        return root == e.Window && WindowQuery.ProcessOf(root) == e.Process;
    }

    private void Watch()
    {
        using var uia = new UiaElements();
        var shell = new Dictionary<int, bool>(); // process id -> is one of Windows' shell programs (read once per process)
        var slow = (Window: IntPtr.Zero, Until: 0L);
        while (Enabled)
        {
            _moved.Wait(); // asleep until the pointer moves
            _moved.Reset();
            if (_entry is { At: Resting } rested) _entry = rested with { At = Environment.TickCount64 }; // moving again: the answer ages again
            var still = 0;
            (int X, int Y)? last = null;
            while (Enabled && still < CoreTiming.AutoscrollRestLooks)
            {
                try
                {
                    if (SystemCommands.GetCursorPos() is { } at)
                    {
                        var settled = last is { } prev && Math.Abs(at.X - prev.X) <= CoreTiming.AutoscrollCachePx && Math.Abs(at.Y - prev.Y) <= CoreTiming.AutoscrollCachePx;
                        still = last == (at.X, at.Y) ? still + 1 : 0;
                        last = (at.X, at.Y);
                        if (settled && (_entry is not { } cached || !Covers(cached, at.X, at.Y)))
                        {
                            // A pointer that has not moved over the same window keeps its answer (renewed, not asked again): a resting pointer
                            // never keeps an app answering UI Automation. The hand pointer still wins at press time (Verdict).
                            if (_entry is { } old && old.X == at.X && old.Y == at.Y && SameWindow(old, at.X, at.Y)) _entry = old with { At = Environment.TickCount64 };
                            else slow = Refresh(uia, shell, slow, at.X, at.Y);
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    _entry = null; // the lookup failed (COM refused, element gone): no answer, so presses pass
                }
                Thread.Sleep(CoreTiming.AutoscrollWatchMs);
            }
            if (_entry is { } kept && last is { } spot && Covers(kept, spot.X, spot.Y)) _entry = kept with { X = spot.X, Y = spot.Y, At = Resting };
        }
    }

    /// Looks under the pointer, one lookup at a time on this worker. Windows' shell (Explorer, the taskbar, Start) and OmniControl itself
    /// are never asked (they have no autoscroll to block, and a UI Automation call waits on the asked program's UI thread); a window whose
    /// answer took longer than <see cref="CoreTiming.AutoscrollSlowMs"/> is not asked again for <see cref="CoreTiming.AutoscrollBackoffMs"/>,
    /// so a busy app is never kept busier. Returns the window being left alone and until when.
    private (IntPtr Window, long Until) Refresh(UiaElements uia, Dictionary<int, bool> shell, (IntPtr Window, long Until) slow, int x, int y)
    {
        var root = WindowQuery.RootWindowAt(x, y);
        if (root == IntPtr.Zero || WindowQuery.ProcessOf(root) is not { } process) { _entry = null; return slow; }
        var started = Environment.TickCount64;
        if (process == Environment.ProcessId || IsShell(shell, process) || PointerShape.IsHandShown() || (root == slow.Window && started < slow.Until))
        {
            _entry = new Entry(root, process, x, y, started, ClickVerdict.Pass);
            return slow;
        }
        var verdict = MiddleClickPolicy.Decide(false, () => uia.ControlTypesAt(x, y, CoreTiming.AutoscrollParentLevels));
        var finished = Environment.TickCount64;
        _entry = new Entry(root, process, x, y, finished, verdict);
        return finished - started > CoreTiming.AutoscrollSlowMs ? (root, finished + CoreTiming.AutoscrollBackoffMs) : slow;
    }

    private static bool IsShell(Dictionary<int, bool> shell, int process)
    {
        if (shell.TryGetValue(process, out var known)) return known;
        if (shell.Count > CoreTiming.AutoscrollProcessCache) shell.Clear();
        return shell[process] = WindowQuery.IsShellProgram(process);
    }
}
