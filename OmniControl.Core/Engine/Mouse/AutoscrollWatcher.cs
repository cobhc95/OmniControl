namespace OmniControl.Core.Engine.Mouse;

/// <summary>
/// Keeps the answer to "may a middle press here be swallowed?" ready before the press, so the mouse hook never waits for a UI Automation
/// lookup (Windows drops a hook that is too slow). While enabled, a worker looks under the pointer whenever it has moved to a spot the
/// cached answer does not cover; <see cref="Verdict"/> only compares the pointer's window, process and position with that answer.
/// No fresh answer means pass (fail open).
/// </summary>
public sealed class AutoscrollWatcher
{
    private sealed record Entry(IntPtr Window, int Process, int X, int Y, long At, ClickVerdict Verdict);

    private readonly object _gate = new();
    private volatile Entry? _entry;
    private bool _enabled;
    private Thread? _worker;

    /// <summary>Starts or stops the worker; stopped, the cache is dropped and every press passes.</summary>
    public void SetEnabled(bool on)
    {
        lock (_gate)
        {
            if (_enabled == on) return;
            _enabled = on;
            if (!on) { _entry = null; return; }
            if (_worker is { IsAlive: true }) return; // the old worker has not noticed it was stopped yet and carries on
            _worker = new Thread(Watch) { IsBackground = true, Name = "AutoscrollWatcher", Priority = ThreadPriority.BelowNormal };
            _worker.Start();
        }
    }

    private bool Enabled { get { lock (_gate) return _enabled; } }

    /// <summary>Runs on the hook thread: reads the cache only, never calls UI Automation. Pass unless a fresh answer for this spot says swallow.</summary>
    public ClickVerdict Verdict(int x, int y)
    {
        if (_entry is not { Verdict: ClickVerdict.Swallow } entry) return ClickVerdict.Pass;
        if (!Covers(entry, x, y)) return ClickVerdict.Pass;
        // The pointer being the hand means a link whatever the cache says; cheap enough for the hook.
        return PointerShape.IsHandShown() ? ClickVerdict.Pass : ClickVerdict.Swallow;
    }

    private static bool Covers(Entry e, int x, int y)
    {
        if (Environment.TickCount64 - e.At > CoreTiming.AutoscrollCacheMs) return false;
        if (Math.Abs(e.X - x) > CoreTiming.AutoscrollCachePx || Math.Abs(e.Y - y) > CoreTiming.AutoscrollCachePx) return false;
        var root = WindowQuery.RootWindowAt(x, y);
        return root == e.Window && WindowQuery.ProcessOf(root) == e.Process;
    }

    private void Watch()
    {
        while (Enabled)
        {
            try
            {
                if (SystemCommands.GetCursorPos() is { } at && (_entry is not { } cached || !Covers(cached, at.X, at.Y))) Refresh(at.X, at.Y);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                _entry = null; // the lookup failed (COM refused, element gone): no answer, so presses pass
            }
            Thread.Sleep(CoreTiming.AutoscrollWatchMs);
        }
    }

    private void Refresh(int x, int y)
    {
        var root = WindowQuery.RootWindowAt(x, y);
        if (root == IntPtr.Zero || WindowQuery.ProcessOf(root) is not { } process) { _entry = null; return; }
        var verdict = MiddleClickPolicy.Decide(false, () => UiaElements.ControlTypesAt(x, y, CoreTiming.AutoscrollParentLevels), CoreTiming.AutoscrollLookupMs);
        _entry = new Entry(root, process, x, y, Environment.TickCount64, verdict);
    }
}
