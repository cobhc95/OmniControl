using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The screens as Windows sees them all together: whether the console's display is on, dimmed or off (told by Windows' display-state notice
/// through <see cref="HardwareWatches"/>, so it is known whoever switched the screens off; DDC cannot see it) and the way to switch them on again.
/// Every display item of the page asks this one object, so a whole-system "screens off" turns every monitor's switch off at once.
/// </summary>
internal sealed class SystemScreens
{
    private readonly HardwareWatches _watches;

    public SystemScreens(HardwareWatches watches) => _watches = watches;

    public bool IsOff => _watches.Screens == ScreenLight.Off;

    /// <summary>Switches every screen on (the same as the input that wakes them); runs off the UI thread.</summary>
    public void WakeUp() => Task.Run(() => Guard.Run("Turning the displays on", () => PowerCommands.TurnOnDisplays()));
}
