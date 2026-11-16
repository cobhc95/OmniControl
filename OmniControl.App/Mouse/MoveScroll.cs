using OmniControl.App.Hotkeys;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>
/// "Change movement to scroll": while it runs the pointer stays where it is (optionally showing the four-arrow pointer) and moving the
/// mouse sends wheel notches, which Windows delivers like a real wheel: to the window under the pointer.
/// The hook side (<see cref="OnMove"/>) only adds up how far the mouse moved; the <see cref="InputQueue"/> thread turns that into
/// notches, sends them and keeps the pointer in place, so the hook never waits on Windows.
/// </summary>
internal sealed class MoveScroll
{
    private sealed record Style(int Distance, int Amount, bool Vertical, bool Sideways, bool ReverseVertical, bool ReverseSideways)
    {
        public static Style From(OptionValues options)
        {
            var direction = options.Text(MouseOptions.ScrollDirection);
            return new Style(options.Int(MouseOptions.ScrollDistance), options.Int(MouseOptions.ScrollAmount),
                direction != MouseOptions.SidewaysOnly, direction != MouseOptions.VerticalOnly,
                options.Bool(MouseOptions.ReverseVertical), options.Bool(MouseOptions.ReverseSideways));
        }
    }

    /// <summary>One press-and-hold (or sticky run). A new session per start, so movement from an old one can never leak into the next.</summary>
    private sealed class Session(int x, int y, Style style)
    {
        public readonly int X = x, Y = y;
        public readonly Style Style = style;
        public int MovedX, MovedY;   // added by the hook thread, taken by the queue thread
        public int NetX, NetY;       // all movement since the press, signed, to tell a click from a drag
        public int RestX, RestY;     // queue thread only: movement not yet worth a whole step
    }

    private readonly InputQueue _queue;
    private readonly Action<string> _diagnose;
    private Session? _session;
    private int _flushQueued;

    /// <param name="diagnose">Adds a line to the mouse log (capped, never blocks).</param>
    public MoveScroll(InputQueue queue, Action<string> diagnose)
    {
        _queue = queue;
        _diagnose = diagnose;
    }

    public bool IsRunning => Volatile.Read(ref _session) is not null;

    /// <summary>Starts with the pointer held at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Start(int x, int y, OptionValues options)
    {
        var session = new Session(x, y, Style.From(options));
        Volatile.Write(ref _session, session);
        _diagnose($"movement-as-scroll on at {x},{y}, {session.Style.Distance} px per step");
    }

    /// <summary>Ends it; false when it was not running.</summary>
    public bool Stop()
    {
        if (Interlocked.Exchange(ref _session, null) is null) return false;
        _diagnose("movement-as-scroll off");
        return true;
    }

    /// <summary>Ends it and says whether the pointer stayed within a scroll step since the press (the press was a click, not a drag); false when it was not running.</summary>
    public bool StopWasClick()
    {
        var session = Volatile.Read(ref _session);
        if (!Stop() || session is null) return false;
        return !ClickOrDrag.IsDrag(Volatile.Read(ref session.NetX), Volatile.Read(ref session.NetY), session.Style.Distance);
    }

    /// <summary>
    /// Runs inside the mouse hook. Returns true to swallow the movement while scrolling: a swallowed movement leaves the pointer where it
    /// was, so the position Windows reports minus the held position is exactly how far the mouse moved, with no need to move it back.
    /// </summary>
    public bool OnMove(int x, int y)
    {
        var session = Volatile.Read(ref _session);
        if (session is null) return false;
        if (x == session.X && y == session.Y) return true;

        Interlocked.Add(ref session.NetX, x - session.X);
        Interlocked.Add(ref session.NetY, y - session.Y);
        Interlocked.Add(ref session.MovedX, x - session.X);
        Interlocked.Add(ref session.MovedY, y - session.Y);
        // One flush queued at a time: a burst of movements becomes one batch of notches instead of a flood of tiny ones.
        if (Interlocked.Exchange(ref _flushQueued, 1) == 0) _queue.Post(Flush);
        return true;
    }

    /// <summary>Queue thread: turns the movement so far into notches and sends them.</summary>
    private void Flush()
    {
        Volatile.Write(ref _flushQueued, 0);
        var session = Volatile.Read(ref _session);
        if (session is null) return;

        _diagnose("  flush");
        session.RestX += Interlocked.Exchange(ref session.MovedX, 0);
        session.RestY += Interlocked.Exchange(ref session.MovedY, 0);
        KeepPointer(session);

        var style = session.Style;
        var down = TakeSteps(ref session.RestY, style.Distance);
        var right = TakeSteps(ref session.RestX, style.Distance);
        // A step on one axis drops the small drift collected on the other, so a slightly crooked pull does not also scroll sideways.
        if (down != 0) session.RestX = 0;
        if (right != 0) session.RestY = 0;

        // Moving down (or right) scrolls down (or right), like turning the wheel towards you.
        if (style.Vertical && down != 0) Send(session, -down * style.Amount * (style.ReverseVertical ? -1 : 1), horizontal: false);
        if (style.Sideways && right != 0) Send(session, right * style.Amount * (style.ReverseSideways ? -1 : 1), horizontal: true);
    }

    /// <summary>Whole steps in <paramref name="rest"/> (signed); what is left stays for next time.</summary>
    private static int TakeSteps(ref int rest, int distance)
    {
        var steps = rest / distance;
        rest -= steps * distance;
        return steps;
    }

    private void Send(Session session, int notches, bool horizontal)
    {
        var posted = WindowCommands.WheelAt(session.X, session.Y, notches, horizontal);
        _diagnose($"  {(horizontal ? "sideways" : "vertical")} scroll {notches:+0;-0} notch(es) posted to the window under the pointer: {(posted ? "ok" : "no window or refused")}");
    }

    /// <summary>If movement slipped through (another program moved the pointer, or Windows skipped the hook), the pointer goes back to the held spot.</summary>
    private void KeepPointer(Session session)
    {
        if (SystemCommands.GetCursorPos() is not { } at || at == (session.X, session.Y)) return;
        _diagnose($"  pointer had moved to {at.X},{at.Y}; put back");
        SystemCommands.SetCursorPos(session.X, session.Y);
    }
}
