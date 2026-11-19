using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Which set of layers an input belongs to: the mouse (buttons, wheel and tilt) or the trackpad (its gestures). Each family has its own
/// layers: names, mappings, the layer in force and the layer actions that move it.
public enum InputFamily { Mouse, Trackpad }

public static class InputFamilies
{
    /// The one place that decides an input's family.
    public static InputFamily FamilyOf(this MouseButton button) => button.IsTrackpadGesture() ? InputFamily.Trackpad : InputFamily.Mouse;

    /// "mouse" or "trackpad", for names and sentences.
    public static string Noun(this InputFamily family) => family == InputFamily.Trackpad ? "trackpad" : "mouse";
}
