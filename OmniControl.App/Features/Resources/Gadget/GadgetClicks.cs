using System.Diagnostics;
using OmniControl.App.Features.Tray;

namespace OmniControl.App.Features.Resources.Gadget;

/// Decides when the gadget's click action runs, so a click is never slower than it must be: at once on the press when no double-click action
/// exists or when the click only opens the tray panel (a double-click then closes it again), else after Windows' double-click time counted from the press.
internal sealed class GadgetClicks : IDisposable
{
    private enum Mode { Wait, Early, EveryPress }

    private readonly Func<GadgetSettings> _look;
    private readonly Func<TimeSpan> _doubleTime;
    private readonly Action<string> _run;
    private IDisposable? _pending;
    private long _pressedAt;
    private Mode _mode;

    /// <param name="doubleTime">Windows' double-click time, asked at each press.</param>
    /// <param name="run">Runs an action by id (the one run path, with its own report).</param>
    public GadgetClicks(Func<GadgetSettings> look, Func<TimeSpan> doubleTime, Action<string> run) => (_look, _doubleTime, _run) = (look, doubleTime, run);

    /// Only the tray panel is harmless to start before the click is known to be single: the double-click's own toggle closes it again.
    private static bool StartsEarly(string actionId) => actionId == TrayFlyoutModel.ToggleActionId;

    /// The left button went down; <paramref name="again"/> is the second press of a double-click.
    public void Pressed(bool again)
    {
        var look = _look();
        if (look.DoubleClickAction.Length == 0)
        {
            _mode = Mode.EveryPress;
            _run(look.ClickAction);
            return;
        }
        if (again) return;
        _pending?.Dispose();
        _pressedAt = Stopwatch.GetTimestamp();
        _mode = StartsEarly(look.ClickAction) ? Mode.Early : Mode.Wait;
        if (_mode == Mode.Early) _run(look.ClickAction);
    }

    /// The button came up without a drag: a waiting click runs once the double-click time since the press has passed.
    public void Released()
    {
        if (_mode != Mode.Wait) return;
        var left = _doubleTime() - Stopwatch.GetElapsedTime(_pressedAt);
        _pending?.Dispose();
        var click = _look().ClickAction;
        _pending = DispatcherTimer.RunOnce(() => _run(click), left > TimeSpan.Zero ? left : TimeSpan.Zero);
    }

    public void DoubleClicked()
    {
        _pending?.Dispose();
        if (_mode == Mode.Early) _run(_look().ClickAction);
        _run(_look().DoubleClickAction);
    }

    public void Dispose() => _pending?.Dispose();
}
