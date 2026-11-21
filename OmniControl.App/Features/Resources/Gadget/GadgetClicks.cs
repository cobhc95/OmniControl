using System.Diagnostics;

namespace OmniControl.App.Features.Resources.Gadget;

/// Decides when the gadget's click action runs: at once on the press when no double-click action exists, else once Windows' double-click time
/// has passed since the press (not the release), so a double-click is always recognised and a single click waits no longer than it must.
internal sealed class GadgetClicks : IDisposable
{
    private readonly Func<GadgetSettings> _look;
    private readonly Func<TimeSpan> _doubleTime;
    private readonly Action<string> _run;
    private IDisposable? _pending;
    private long _pressedAt;
    private bool _waits, _second;

    /// <param name="doubleTime">Windows' double-click time, asked at each press.</param>
    /// <param name="run">Runs an action by id (the one run path, with its own report).</param>
    public GadgetClicks(Func<GadgetSettings> look, Func<TimeSpan> doubleTime, Action<string> run) => (_look, _doubleTime, _run) = (look, doubleTime, run);

    /// The left button went down; <paramref name="again"/> is the second press of a double-click.
    public void Pressed(bool again)
    {
        var look = _look();
        _waits = look.DoubleClickAction.Length > 0;
        if (!_waits)
        {
            _run(look.ClickAction);
            return;
        }
        _second = again;
        if (again) return;
        _pending?.Dispose();
        _pressedAt = Stopwatch.GetTimestamp();
    }

    /// The button came up without a drag: the click runs once the double-click time since the press has passed (never after a double-click's second press).
    public void Released()
    {
        if (!_waits || _second) return;
        var left = _doubleTime() - Stopwatch.GetElapsedTime(_pressedAt);
        _pending?.Dispose();
        var click = _look().ClickAction;
        _pending = DispatcherTimer.RunOnce(() => _run(click), left > TimeSpan.Zero ? left : TimeSpan.Zero);
    }

    public void DoubleClicked()
    {
        _pending?.Dispose();
        _run(_look().DoubleClickAction);
    }

    public void Dispose() => _pending?.Dispose();
}
