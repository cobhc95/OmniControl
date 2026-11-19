namespace OmniControl.Core.Engine.Trackpad;

/// Windows' own panels that its touchpad gestures open (a gesture's <see cref="TrackpadGesture.Opens"/>).
[Flags]
public enum ShellSurface
{
    None = 0,
    Search = 1,
    NotificationCentre = 2,
    TaskView = 4,
}

/// One of Windows' panels and how its window is known: the processes that show it and, when those show other windows too, the window
/// classes (null = any window of those processes; they show nothing but such panels).
public sealed record ShellSurfaceKind(ShellSurface Surface, string Name, IReadOnlyList<string> Processes, IReadOnlyList<string>? Classes);

/// The master list of Windows' panels the safety net knows (read on Windows 11 build 26300; the older names are Windows 10's).
public static class ShellSurfaces
{
    public static readonly IReadOnlyList<ShellSurfaceKind> All = new ShellSurfaceKind[]
    {
        new(ShellSurface.Search, "Search", new[] { "SearchHost", "SearchApp" }, null),
        new(ShellSurface.NotificationCentre, "the notification centre", new[] { "ShellExperienceHost", "ShellHost" }, null),
        new(ShellSurface.TaskView, "Task view", new[] { "explorer" }, new[] { "XamlExplorerHostIslandWindow", "MultitaskingViewFrame" }),
    };

    /// The panel a window of this process and class is, or <see cref="ShellSurface.None"/>.
    public static ShellSurface Of(string process, string windowClass)
    {
        foreach (var kind in All)
        {
            if (!kind.Processes.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;
            if (kind.Classes is null || kind.Classes.Contains(windowClass, StringComparer.Ordinal)) return kind.Surface;
        }
        return ShellSurface.None;
    }

    public static string NameOf(ShellSurface surface) => All.FirstOrDefault(k => k.Surface == surface)?.Name ?? surface.ToString();
}

/// What the safety net needs from Windows: the window in front, which panel a window is, and closing one. Tests use a fake.
public interface IShellSurfaceProbe
{
    /// The top-level window in front now, or zero.
    IntPtr Foreground();

    /// Which of Windows' panels this window is, or <see cref="ShellSurface.None"/>.
    ShellSurface SurfaceOf(IntPtr window);

    /// Closes the panel the way the user would (Esc), only while it is still in front. False when it no longer was.
    bool Dismiss(IntPtr window);
}

/// The safety net behind the takeover (<see cref="GestureTakeover"/>), never the main path: when a gesture whose Windows action is
/// supposed to be off was recognised, it watches the window in front for <see cref="CoreTiming.TrackpadSurfaceWatchMs"/>; if one of the
/// panels Windows' action opens (<see cref="TrackpadGesture.Opens"/>) comes to the front within that time, it is closed with Esc. It never
/// touches a panel that was already in front when the gesture was made, a window that is not one of those panels, or anything after the
/// time is up, and it closes at most <see cref="CoreTiming.TrackpadSurfaceDismissLimit"/> per minute. Whether a gesture is watched at all
/// (an action that replaces Windows' gesture, the "also let Windows do its own gesture" option off) is the caller's decision. One watch at
/// a time: a newer gesture replaces it. Thread-safe.
public sealed class ShellSurfaceGuard
{
    private sealed record Watch(string Gesture, ShellSurface Surfaces, IntPtr Before, DateTime Since);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly IShellSurfaceProbe _probe;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Queue<DateTime> _dismissed = new();
    private Watch? _watch;
    private bool _limitLogged;

    public ShellSurfaceGuard(IShellSurfaceProbe probe, Func<DateTime>? now = null)
    {
        _probe = probe;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// Gets one line per panel closed (or left because of the limit), for the trackpad log.
    public Action<string>? Log { get; init; }

    /// A gesture whose Windows action is supposed to be off was recognised now: watch for <paramref name="surfaces"/>. Returns false (and
    /// watches nothing) when there is nothing to watch for, or one of those panels is already in front.
    public bool Arm(string gesture, ShellSurface surfaces)
    {
        if (surfaces == ShellSurface.None) return false;
        var before = _probe.Foreground();
        if (before != IntPtr.Zero && (_probe.SurfaceOf(before) & surfaces) != 0) return false; // opened before the gesture: the user's
        lock (_gate) _watch = new Watch(gesture, surfaces, before, _now());
        return true;
    }

    /// True while a gesture is being watched.
    public bool IsWatching
    {
        get
        {
            lock (_gate) return _watch is not null;
        }
    }

    /// One look at the window in front. True while still watching (the caller looks again shortly), false once done. Several callers may
    /// look at once: only one of them closes a panel.
    public bool Look()
    {
        Watch? watch;
        lock (_gate) watch = _watch;
        if (watch is null) return false;
        var elapsed = _now() - watch.Since;
        if (elapsed.TotalMilliseconds > CoreTiming.TrackpadSurfaceWatchMs)
        {
            Claim(watch);
            return IsWatching;
        }

        var window = _probe.Foreground();
        if (window == IntPtr.Zero || window == watch.Before) return true;
        var surface = _probe.SurfaceOf(window) & watch.Surfaces;
        if (surface == ShellSurface.None) return true; // another window came to the front (the action's own, say): keep watching
        if (!Claim(watch)) return IsWatching; // another look took it

        if (MayDismiss())
        {
            var closed = _probe.Dismiss(window);
            Log?.Invoke($"{watch.Gesture}: Windows opened {ShellSurfaces.NameOf(surface)} {elapsed.TotalMilliseconds:0} ms later although its own gesture is off; "
                        + (closed ? "closed it (Esc)" : "it was no longer in front, left alone"));
        }
        else if (FirstAtLimit())
            Log?.Invoke($"{watch.Gesture}: Windows opened {ShellSurfaces.NameOf(surface)}; left open, {CoreTiming.TrackpadSurfaceDismissLimit} were closed this minute already");
        return IsWatching;
    }

    /// Ends this watch; false when it had already ended or a newer gesture's watch replaced it (which stays).
    private bool Claim(Watch watch)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_watch, watch)) return false;
            _watch = null;
            return true;
        }
    }

    private bool MayDismiss()
    {
        lock (_gate)
        {
            var now = _now();
            while (_dismissed.Count > 0 && now - _dismissed.Peek() > Minute) _dismissed.Dequeue();
            if (_dismissed.Count >= CoreTiming.TrackpadSurfaceDismissLimit) return false;
            _dismissed.Enqueue(now);
            _limitLogged = false;
            return true;
        }
    }

    /// True the first time the limit stops a close (one log line per run of refusals).
    private bool FirstAtLimit()
    {
        lock (_gate)
        {
            if (_limitLogged) return false;
            _limitLogged = true;
            return true;
        }
    }
}
