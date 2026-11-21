using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The Trackpad group: one card per gesture of <see cref="TrackpadGestures"/> (each built like a mouse button's card: same picker and
/// settings, on the trackpad's own layers, picked on the group's title line), then the card that opens Windows' touchpad settings. The
/// gesture with an action always replaces Windows' own (no switch), unless that action's
/// settings tick "Also let Windows do its own gesture". The cards say what is true now: greyed with the reason when no gesture can be
/// seen, and tagged (only once the gesture has an action) when Windows' own one still runs on purpose or could not be turned off.
public sealed partial class MousePageViewModel
{
    private const string TrackpadKeywords = "trackpad touchpad gestures swipe fingers pinch tap double tap layer preset apps scope";
    /// The Trackpad group's name, also its layer preset group's card (<see cref="InputActions"/>).
    public const string TrackpadGroupName = "Trackpad";

    /// The apps the Trackpad group works in: the "Apps: ..." chip on its title line, left of the layer picker.
    public AppScopeModel TrackpadScope { get; }

    private readonly TrackpadService _trackpad;
    private IReadOnlyList<(MouseButtonItem Card, TrackpadGesture Gesture)> _gestureCards = Array.Empty<(MouseButtonItem, TrackpadGesture)>();

    /// The trackpad gesture cards, in a collapsible group under the mouse buttons.
    public ItemGroup TrackpadGroup { get; private set; } = null!;

    private ItemGroup BuildTrackpadGroup(IReadOnlyList<MouseButtonItem> cards, Action<string> report)
    {
        var windowsSettings = new ButtonItem("Windows touchpad settings", "Open",
            "Windows' own swipes, pinch and taps.",
            () => { if (WebPage.Open(WindowsTouchpad.SettingsPage) is { } problem) report(problem); }) { Half = true };

        _gestureCards = cards.Zip(TrackpadGestures.All).ToList();
        _followed.Follow<Action>(h => _trackpad.Changed += h, h => _trackpad.Changed -= h, ShowTrackpadState);
        ShowTrackpadState();

        // Last, so the gesture cards keep their pairs from the first row.
        var items = cards.Cast<object>().Append(windowsSettings).ToList();
        return new ItemGroup("Mouse/Trackpad", TrackpadGroupName, items)
        {
            ExpandedByDefault = true,
            Tip = "Swipes, pinches and taps on a precision touchpad; Default lets Windows do what it normally does. "
                  + "The layer drop-down on this line picks the trackpad's own layer, separate from the mouse's. "
                  + "Outside its apps a gesture does nothing; Windows' own gesture stays off while the gesture has an action. "
                  + "OmniControl can't block Windows' own gesture, so while a gesture has an action it turns Windows' one off and gives it back afterwards; "
                  + "tick \"Also let Windows do its own gesture\" in an action's settings to keep both. "
                  + "A double tap replaces the second tap of the pair. One-finger taps are also clicks, so give that card an action with care. Two-finger scrolling arrives as Wheel and Tilt.",
        };
    }

    private void ShowTrackpadState()
    {
        var reason = _trackpad.UnavailableReason;
        foreach (var (card, gesture) in _gestureCards)
        {
            if (reason is null) card.Availability.MakeAvailable();
            else card.Availability.MakeUnavailable(reason);
            ShowWindowsSide(card, gesture.AlsoWindows);
        }
    }

    /// The card's tag and its tooltip. Turning Windows' own gesture off is what remapping is expected to do, so it is not tagged; a tag is
    /// shown only for what is not obvious: Windows' own gesture still runs because this action asked for it, or turning it off failed.
    private void ShowWindowsSide(MouseButtonItem card, WindowsGesture gesture)
    {
        var setting = WindowsGestureSettings.Of(gesture);
        var status = _trackpad.TakeoverOf(gesture);
        if (status is { State: TakeoverState.Failed, Problem: { } problem })
        {
            card.WindowsState = WindowsTag.Problem;
            card.WindowsDetail = $"{problem} Windows' {setting.Name} still runs (it {setting.WindowsAction}), so both happen.";
            return;
        }
        // Another gesture that shares this Windows setting may replace it, so ticking the option alone does not mean Windows' one runs.
        var also = card.KeepsOriginal && _trackpad.Windows.Uses(gesture);
        card.WindowsState = also ? WindowsTag.AlsoRuns : WindowsTag.None;
        card.WindowsDetail = also
            ? $"Windows' {setting.Name} also runs (it {setting.WindowsAction}), as this action's settings ask (Also let Windows do its own gesture)."
            : string.Empty;
    }
}
