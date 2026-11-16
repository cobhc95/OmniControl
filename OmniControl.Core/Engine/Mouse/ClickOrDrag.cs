namespace OmniControl.Core.Engine.Mouse;

/// <summary>Tells a held button that stayed a click from one that was used as a drag (movement turned into scrolling).</summary>
public static class ClickOrDrag
{
    /// <summary>A drag once the pointer has moved a whole scroll step or more, on either axis, since the press (net movement, so jitter cancels).</summary>
    public static bool IsDrag(int netX, int netY, int stepDistance) =>
        Math.Abs(netX) >= stepDistance || Math.Abs(netY) >= stepDistance;
}
