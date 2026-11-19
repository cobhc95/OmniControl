using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Back and Forward over OmniControl's own windows or the desktop are swallowed (press and release) when no handler before it took them:
/// nothing there can go back or forward, and Windows hands an unused side click on to the shell as a "browser back/forward" command, and
/// each pass-through Back once started a new Explorer folder-window process until the shell crashed. It is the hook's last handler, and
/// it always needs the hook (it watches wherever the pointer is), so the hook stays installed for it.
internal sealed class BackForwardGuard
{
    private readonly HashSet<MouseButton> _swallowed = new(); // hook thread only: a swallowed press whose release must be swallowed too
    private readonly Action<string> _log;

    /// <param name="hook">The shared mouse hook; add this after every other handler so a mapping or rule for Back or Forward still wins.</param>
    public BackForwardGuard(MouseHook hook)
    {
        _log = hook.Diagnose;
        hook.AddHandler(Handle, () => true);
    }

    private bool Handle(MouseEvent ev)
    {
        if (ev.Button is not (MouseButton.Back or MouseButton.Forward)) return false;
        if (!ev.IsDown) return _swallowed.Remove(ev.Button);
        var nowhereToGo = OwnProgram.IsUnder(ev.X, ev.Y) || PointerArea.IsDesktop(ev.X, ev.Y);
        if (!nowhereToGo)
        {
            _swallowed.Remove(ev.Button); // a press whose release never arrived
            return false;
        }
        _log($"{ev.Button} swallowed over OmniControl or the desktop (no back or forward there)");
        _swallowed.Add(ev.Button);
        return true;
    }
}
