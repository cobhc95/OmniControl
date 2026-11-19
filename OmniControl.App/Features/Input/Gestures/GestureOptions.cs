using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// The options a wheel gesture's action can offer (behind the cogwheel), defined once.
public static class GestureOptions
{
    public static readonly ActionOption Reverse = ActionOption.Toggle("reverse", "Reverse the direction (wheel up does the second thing)");
    public static readonly ActionOption Amount = ActionOption.Number("amount", "Change per wheel notch", Hotkeys.HotkeyAction.DefaultStep, 1, 25, 1, " %");
    public static readonly ActionOption Steps = ActionOption.Number("steps", "Steps per wheel notch", 1, 1, 10);
    public static readonly ActionOption OpacityStep = ActionOption.Number("opacity", "Opacity change per wheel notch", 5, 1, 50, 1, " %");
}
