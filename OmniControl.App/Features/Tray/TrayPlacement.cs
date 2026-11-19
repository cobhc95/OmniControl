namespace OmniControl.App.Features.Tray;

/// Where the tray flyout goes, in screen pixels (pure, no UI): beside the taskbar the click came from, over the tray icon, never past the
/// working area of the screen it was clicked on. The taskbar is wherever the click is outside the working area: below it (the usual
/// place), above it, or at its left or right; a click inside it (a hidden taskbar) puts the flyout's corner at the click.
public static class TrayPlacement
{
    /// <param name="click">Where the tray icon was clicked.</param>
    /// <param name="work">The working area of the screen the click is on (the screen less the taskbar).</param>
    /// <param name="size">The flyout's size.</param>
    /// <param name="gap">The space kept between the flyout and the working area's edges.</param>
    public static PixelPoint Place(PixelPoint click, PixelRect work, PixelSize size, int gap)
    {
        var x = click.X >= work.Right ? work.Right - size.Width - gap
            : click.X < work.X ? work.X + gap
            : click.X - size.Width / 2;
        var y = click.Y >= work.Bottom ? work.Bottom - size.Height - gap
            : click.Y < work.Y ? work.Y + gap
            : click.Y - size.Height;
        return new PixelPoint(Clamp(x, work.X + gap, work.Right - size.Width - gap), Clamp(y, work.Y + gap, work.Bottom - size.Height - gap));
    }

    // A flyout larger than the area keeps its top-left corner inside it.
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
