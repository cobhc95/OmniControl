namespace OmniControl.Core.Engine.Mouse;

/// <summary>
/// Every input the remapper can see: the mouse's buttons, wheel and tilt, and the trackpad gestures of
/// <see cref="Trackpad.TrackpadGestures"/>. The names are stored in mouse.json ("1.Middle", "1.ThreeFingerUp"): never rename one.
/// Wheel, tilt and gestures have no "up": they only fire.
/// </summary>
public enum MouseButton
{
    Left, Right, Middle, Back, Forward, WheelUp, WheelDown, TiltLeft, TiltRight,
    ThreeFingerUp, ThreeFingerDown, ThreeFingerLeft, ThreeFingerRight,
    FourFingerUp, FourFingerDown, FourFingerLeft, FourFingerRight,
    PinchIn, PinchOut,
    OneFingerTap, TwoFingerTap, ThreeFingerTap, OneFingerDoubleTap, TwoFingerDoubleTap, ThreeFingerDoubleTap,
}

/// <summary>One mouse input seen by the hook. <see cref="IsDown"/> is always true for inputs without a release (wheel, tilt, gestures).</summary>
public readonly record struct MouseEvent(MouseButton Button, bool IsDown, int X, int Y);

/// <summary>The one rule for which inputs are pressed and released, and which only fire.</summary>
public static class MouseButtons
{
    /// <summary>True for the buttons that are pressed and later released; false for wheel, tilt and trackpad gestures, which only fire.</summary>
    public static bool HasRelease(this MouseButton button) =>
        button is MouseButton.Left or MouseButton.Right or MouseButton.Middle or MouseButton.Back or MouseButton.Forward;

    /// <summary>True for the trackpad gestures: they come from the touchpad's raw input, so unlike a click they can never be swallowed.</summary>
    public static bool IsTrackpadGesture(this MouseButton button) => button >= MouseButton.ThreeFingerUp;
}
