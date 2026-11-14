using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The Trackpad group: its on/off switch, the card that opens Windows' touchpad settings, and one card per gesture of
/// <see cref="TrackpadGestures"/>, each built like a mouse button's card (same picker, layers and settings). The cards say what is
/// true now: greyed with the reason when no gesture can be seen, and tagged when Windows also runs its own action for the gesture.
/// </summary>
public sealed partial class MousePageViewModel
{
    private const string TrackpadKeywords = "trackpad touchpad gestures swipe fingers pinch";

    private readonly TrackpadService _trackpad;
    private ToggleItem _trackpadOn = null!; // made by BuildTrackpadGroup, from the constructor
    private IReadOnlyList<(MouseButtonItem Card, TrackpadGesture Gesture)> _gestureCards = Array.Empty<(MouseButtonItem, TrackpadGesture)>();

    /// <summary>The trackpad gesture cards, in a collapsible group under the mouse buttons.</summary>
    public ItemGroup TrackpadGroup { get; private set; } = null!;

    private ItemGroup BuildTrackpadGroup(IReadOnlyList<MouseButtonItem> cards, Action<string> report)
    {
        _trackpadOn = new ToggleItem("OmniControl trackpad gestures", _trackpad.IsEnabled, _trackpad.SetEnabled) { Half = true };
        var windowsSettings = new ButtonItem("Windows touchpad settings", "Open",
            "Turn off Windows' own swipes and pinch there, so only OmniControl's action runs.",
            () => { if (WebPage.Open(WindowsTouchpad.SettingsPage) is { } problem) report(problem); }) { Half = true };

        _gestureCards = cards.Zip(TrackpadGestures.All).ToList();
        _trackpad.Changed += ShowTrackpadState;
        ShowTrackpadState();

        var items = new object[] { _trackpadOn, windowsSettings }.Concat(cards).ToList();
        return new ItemGroup("Mouse/Trackpad", "Trackpad", items)
        {
            ExpandedByDefault = true,
            Tip = "Three- or four-finger swipes and pinches on a precision touchpad; the Layer drop-down applies here too, and Default lets Windows do what it normally does. "
                  + "OmniControl can't stop Windows' own swipes or pinch to zoom: if both happen, turn Windows' gesture off in its touchpad settings. "
                  + "Two-finger scrolling arrives as Wheel and Tilt.",
        };
    }

    private void ShowTrackpadState()
    {
        if (_trackpadOn.IsOn != _trackpad.IsEnabled) _trackpadOn.IsOn = _trackpad.IsEnabled; // a hotkey changed it
        var reason = _trackpad.UnavailableReason;
        foreach (var (card, gesture) in _gestureCards)
        {
            if (reason is null) card.Availability.MakeAvailable();
            else card.Availability.MakeUnavailable(reason);
            card.WindowsAlso = _trackpad.Windows.Uses(gesture.AlsoWindows);
        }
    }
}
