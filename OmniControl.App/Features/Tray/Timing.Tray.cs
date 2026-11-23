namespace OmniControl.App.Shared.Services;

// The defaults of the tray flyout's intervals and limits; the user's own values are in TraySettings (App settings), a part of Timing in Shared/Services.
public static partial class Timing
{
    /// A click on the tray icon this soon after the flyout hid itself (it hides as the press on the icon takes the focus away) only closed it:
    /// the click does not open it again, so the icon opens and closes the flyout like Windows' own tray flyouts.
    public static readonly TimeSpan TrayReopenGrace = TimeSpan.FromMilliseconds(400);

    /// Opening the panel reads its devices' states one at a time, this long apart (Tray/TrayFlyoutModel.Rechecks), so opening stays instant.
    public static readonly TimeSpan TrayRecheckStagger = TimeSpan.FromMilliseconds(400);

    /// A tile's state is never read again sooner than this after the last read was asked for (a quick re-open of the panel asks nothing).
    public static readonly TimeSpan TrayRecheckMinGap = TimeSpan.FromSeconds(10);
}
