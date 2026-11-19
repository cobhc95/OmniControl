using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Tray;

/// Tells the second click of a double click on the tray icon from a single one, without making the single click wait: the first click acts
/// at once, and a second press or click by Windows' own rule (the user's double-click time from the first click, inside the double-click
/// rectangle around it, <see cref="SystemCommands.DoubleClick"/>) is the double click.
internal sealed class TrayClicks
{
    private DateTime _first = DateTime.MinValue;
    private (int X, int Y)? _at;

    /// A click on the icon at the pointer's place: true when it is the second of a double click, else it may be the first of one.
    public bool Click((int X, int Y)? at)
    {
        if (IsSecond(at)) return true;
        (_first, _at) = (DateTime.UtcNow, at);
        return false;
    }

    /// True when a press or click at <paramref name="at"/> is the second of a double click (the next click then starts a new count).
    public bool IsSecond((int X, int Y)? at)
    {
        var (ms, width, height) = SystemCommands.DoubleClick();
        var second = Grace.Within(_first, TimeSpan.FromMilliseconds(ms)) && at is { } p && _at is { } first
            && Math.Abs(p.X - first.X) <= width / 2 && Math.Abs(p.Y - first.Y) <= height / 2;
        if (second) _first = DateTime.MinValue;
        return second;
    }
}
