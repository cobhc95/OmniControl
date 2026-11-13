using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>How the fingers move in a trackpad gesture.</summary>
public enum GestureMotion { Up, Down, Left, Right, PinchIn, PinchOut }

/// <summary>Windows' own touchpad settings that also act on a gesture (Settings > Bluetooth &amp; devices > Touchpad).</summary>
public enum WindowsGesture { ThreeFingerSwipes, FourFingerSwipes, PinchToZoom }

/// <summary>
/// One trackpad gesture: the input value it arrives as (<see cref="Button"/>, also its stored key), a stable <see cref="Id"/> (rule
/// triggers store it), the card's <see cref="Name"/>, how it reads in a rule's sentence (<see cref="Phrase"/>), what the fingers do,
/// and which Windows setting also acts on it.
/// </summary>
public sealed record TrackpadGesture(MouseButton Button, string Id, string Name, string Phrase, int Fingers, GestureMotion Motion, WindowsGesture AlsoWindows);

/// <summary>
/// The master list of trackpad gestures: one registration per gesture, read by the recogniser, the Mouse and Trackpad page (one card
/// each), the custom rules' triggers and the "Windows also does this" check. Two-finger scroll is not here on purpose: it reaches
/// OmniControl as wheel and tilt input, which the mouse button cards already handle.
/// </summary>
public static class TrackpadGestures
{
    public static readonly IReadOnlyList<TrackpadGesture> All = new TrackpadGesture[]
    {
        new(MouseButton.ThreeFingerUp, "three.up", "Three fingers up", "swipe three fingers up", 3, GestureMotion.Up, WindowsGesture.ThreeFingerSwipes),
        new(MouseButton.ThreeFingerDown, "three.down", "Three fingers down", "swipe three fingers down", 3, GestureMotion.Down, WindowsGesture.ThreeFingerSwipes),
        new(MouseButton.ThreeFingerLeft, "three.left", "Three fingers left", "swipe three fingers left", 3, GestureMotion.Left, WindowsGesture.ThreeFingerSwipes),
        new(MouseButton.ThreeFingerRight, "three.right", "Three fingers right", "swipe three fingers right", 3, GestureMotion.Right, WindowsGesture.ThreeFingerSwipes),
        new(MouseButton.FourFingerUp, "four.up", "Four fingers up", "swipe four fingers up", 4, GestureMotion.Up, WindowsGesture.FourFingerSwipes),
        new(MouseButton.FourFingerDown, "four.down", "Four fingers down", "swipe four fingers down", 4, GestureMotion.Down, WindowsGesture.FourFingerSwipes),
        new(MouseButton.FourFingerLeft, "four.left", "Four fingers left", "swipe four fingers left", 4, GestureMotion.Left, WindowsGesture.FourFingerSwipes),
        new(MouseButton.FourFingerRight, "four.right", "Four fingers right", "swipe four fingers right", 4, GestureMotion.Right, WindowsGesture.FourFingerSwipes),
        new(MouseButton.PinchIn, "pinch.in", "Pinch in", "pinch in", 2, GestureMotion.PinchIn, WindowsGesture.PinchToZoom),
        new(MouseButton.PinchOut, "pinch.out", "Pinch out", "pinch out", 2, GestureMotion.PinchOut, WindowsGesture.PinchToZoom),
    };

    /// <summary>The gesture made with this many fingers and this motion, or null (the recogniser asks; a loop, so nothing is allocated).</summary>
    public static TrackpadGesture? Find(int fingers, GestureMotion motion)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i].Fingers == fingers && All[i].Motion == motion) return All[i];
        return null;
    }
}
