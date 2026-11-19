namespace OmniControl.App.Features.Displays.Pages;

/// The screens as Windows sees them all together: whether the console's display is on, dimmed or off (told by Windows' display-state notice
/// through <see cref="HardwareWatches"/>, so it is known whoever switched the screens off; DDC cannot see it) and the way to switch them on again.
/// Every display item of the page asks this one object, so a whole-system "screens off" turns every monitor's switch off at once.
internal sealed class SystemScreens
{
    private readonly IHardwareWatches _watches;

    public SystemScreens(IHardwareWatches watches) => _watches = watches;

    public bool IsOff => _watches.Screens == ScreenLight.Off;

    /// Switches every screen on (the same as the input that wakes them); runs off the UI thread.
    public void WakeUp() => Task.Run(() => Guard.Run("Turning the displays on", () => PowerCommands.TurnOnDisplays()));
}
