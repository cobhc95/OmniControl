namespace OmniControl.Core.Engine.Mouse;

public enum ClickVerdict { Pass, Swallow }

/// Decides what happens to a middle press no action uses, when apps' own autoscroll is blocked: it passes through over a link, a tab,
/// a button or a menu item (or while the pointer is the hand) so "open in new tab" and "close tab" keep working, and is swallowed
/// anywhere else. Whatever cannot be decided in time or at all passes (fail open), so the button is never lost for good.
public static class MiddleClickPolicy
{
    // UI Automation control type ids: Button, Hyperlink, MenuItem, TabItem.
    private static readonly HashSet<int> PassingTypes = new() { 50000, 50005, 50011, 50019 };

    /// True when the element under the pointer, or one of its checked parents, is a kind a middle click means something on.
    public static bool IsLinkLike(IEnumerable<int> controlTypes) => controlTypes.Any(PassingTypes.Contains);

    /// <param name="handCursor">The system pointer at the time of the press is the hand.</param>
    /// <param name="lookup">
    /// The control types from the element under the pointer upwards, or null when they could not be read. Runs on the calling thread (the
    /// autoscroll watcher's own worker), so there is only ever one lookup at a time and none piles up behind a slow app.
    /// </param>
    public static ClickVerdict Decide(bool handCursor, Func<IReadOnlyList<int>?> lookup)
    {
        if (handCursor) return ClickVerdict.Pass;
        try
        {
            return lookup() is { } types && !IsLinkLike(types) ? ClickVerdict.Swallow : ClickVerdict.Pass;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return ClickVerdict.Pass; // the lookup failed (COM refused, element gone): fail open
        }
    }
}
