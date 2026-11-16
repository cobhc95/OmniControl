namespace OmniControl.Core.Engine.Mouse;

public enum ClickVerdict { Pass, Swallow }

/// <summary>
/// Decides what happens to a middle press no action uses, when apps' own autoscroll is blocked: it passes through over a link, a tab,
/// a button or a menu item (or while the pointer is the hand) so "open in new tab" and "close tab" keep working, and is swallowed
/// anywhere else. Whatever cannot be decided in time or at all passes (fail open), so the button is never lost for good.
/// </summary>
public static class MiddleClickPolicy
{
    // UI Automation control type ids: Button, Hyperlink, MenuItem, TabItem.
    private static readonly HashSet<int> PassingTypes = new() { 50000, 50005, 50011, 50019 };

    /// <summary>True when the element under the pointer, or one of its checked parents, is a kind a middle click means something on.</summary>
    public static bool IsLinkLike(IEnumerable<int> controlTypes) => controlTypes.Any(PassingTypes.Contains);

    /// <param name="handCursor">The system pointer at the time of the press is the hand.</param>
    /// <param name="lookup">The control types from the element under the pointer upwards, or null when they could not be read. Runs off the calling thread.</param>
    /// <param name="timeoutMs">How long to wait for <paramref name="lookup"/>.</param>
    public static ClickVerdict Decide(bool handCursor, Func<IReadOnlyList<int>?> lookup, int timeoutMs)
    {
        if (handCursor) return ClickVerdict.Pass;
        try
        {
            var task = Task.Run(lookup);
            if (!task.Wait(timeoutMs) || task.Result is not { } types) return ClickVerdict.Pass;
            return IsLinkLike(types) ? ClickVerdict.Pass : ClickVerdict.Swallow;
        }
        catch (AggregateException)
        {
            return ClickVerdict.Pass; // the lookup failed (COM refused, element gone): fail open
        }
    }

    /// <summary>The usual lookup: the pointer's shape and the control types at a screen point.</summary>
    public static ClickVerdict DecideAt(int x, int y) =>
        Decide(PointerShape.IsHandShown(), () => UiaElements.ControlTypesAt(x, y, CoreTiming.AutoscrollParentLevels), CoreTiming.AutoscrollLookupMs);
}
