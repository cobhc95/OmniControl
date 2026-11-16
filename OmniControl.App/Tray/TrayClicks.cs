using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Tray;

/// <summary>
/// Tells the second click of a double click on the tray icon from a single one, without making the single click wait: the first click acts
/// at once, and a second within Windows' own double-click time (the user's mouse setting, <see cref="SystemCommands.DoubleClick"/>, read at
/// each click) is the double click.
/// </summary>
internal sealed class TrayClicks
{
    private DateTime _last = DateTime.MinValue;

    /// <summary>Counts a click; true when it is the second of a double click.</summary>
    public bool IsDouble()
    {
        var isDouble = Grace.Within(_last, TimeSpan.FromMilliseconds(SystemCommands.DoubleClick().Milliseconds));
        _last = isDouble ? DateTime.MinValue : DateTime.UtcNow;
        return isDouble;
    }
}
