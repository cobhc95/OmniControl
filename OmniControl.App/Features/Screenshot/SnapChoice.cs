namespace OmniControl.App.Features.Screenshot;

/// The overlay's magnet: the "Snap to windows" setting of the action's settings in use, switched from the overlay and kept for the next
/// screenshot. <c>save</c> writes the settings.
public sealed class SnapChoice(ScreenshotOptions options, Action save)
{
    public bool On => options.SnapToWindows;

    public void Toggle()
    {
        options.SnapToWindows = !options.SnapToWindows;
        save();
    }
}
