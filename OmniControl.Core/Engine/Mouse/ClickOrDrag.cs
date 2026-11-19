namespace OmniControl.Core.Engine.Mouse;

/// Tells a held button that stayed a click from one that was used as a drag (movement turned into scrolling).
public static class ClickOrDrag
{
    /// A drag once the pointer has moved a whole scroll step or more, on either axis, since the press (net movement, so jitter cancels).
    public static bool IsDrag(int netX, int netY, int stepDistance) =>
        Math.Abs(netX) >= stepDistance || Math.Abs(netY) >= stepDistance;
}
