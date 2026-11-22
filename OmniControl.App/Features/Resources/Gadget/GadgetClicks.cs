using System.Diagnostics;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Resources.Gadget;

/// Decides when the gadget's click action runs: at once on the press when no double-click action exists, else once the double-click time has
/// passed since the press (not the release), so a double-click is always recognised and a single click waits no longer than it must.
/// The double-click is decided here (a second press within the double-click time from the first), not by the UI framework's click count:
/// that count is reset when the layered taskbar window is redrawn or re-placed between the presses.
internal sealed class GadgetClicks : IDisposable
{
    private readonly Func<GadgetSettings> _look;
    private readonly Action<string> _run;
    private IDisposable? _pending;
    private long _pressedAt;
    private bool _waits, _second, _armed;

    /// <param name="run">Runs an action by id (the one run path, with its own report).</param>
    public GadgetClicks(Func<GadgetSettings> look, Action<string> run) => (_look, _run) = (look, run);

    /// The double-click time in force: the gadget's own setting, or Windows' when it has none.
    public static TimeSpan DoubleTime(GadgetSettings look) =>
        TimeSpan.FromMilliseconds(look.DoubleClickMs > 0 ? look.DoubleClickMs : SystemCommands.DoubleClick().Milliseconds);

    /// A second press pairs with the first when it comes within the double-click time: the gadget is a small target with no drag, so where
    /// on it the press lands does not matter (Windows' few-pixel rectangle made a slightly moving mouse miss).
    internal static bool Pairs(TimeSpan sincePress, TimeSpan doubleTime) => sincePress <= doubleTime;

    /// The left button went down on the gadget.
    public void Pressed()
    {
        var look = _look();
        _waits = look.DoubleClickAction.Length > 0;
        if (!_waits)
        {
            _run(look.ClickAction);
            return;
        }
        _second = _armed && Pairs(Stopwatch.GetElapsedTime(_pressedAt), DoubleTime(look));
        _pending?.Dispose();
        if (_second)
        {
            _armed = false;
            _run(look.DoubleClickAction);
            return;
        }
        (_armed, _pressedAt) = (true, Stopwatch.GetTimestamp());
    }

    /// The button came up: the click runs once the double-click time since the press has passed (never after a double-click's second press).
    public void Released()
    {
        if (!_waits || _second) return;
        var left = DoubleTime(_look()) - Stopwatch.GetElapsedTime(_pressedAt);
        _pending?.Dispose();
        var click = _look().ClickAction;
        _pending = DispatcherTimer.RunOnce(() => _run(click), left > TimeSpan.Zero ? left : TimeSpan.Zero);
    }

    public void Dispose() => _pending?.Dispose();
}
