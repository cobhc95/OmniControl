using Avalonia.Threading;

namespace OmniControl.App.Services;

/// <summary>
/// Draw first, wire later: what the user sees (names, headings, the layout, the last known values) is shown at once, and the live
/// "wiring" (hardware reads, state subscriptions, a page's refresh) runs after the frame is drawn, below input, so it never holds up
/// the first frame or a key press. Values filled in later keep their place, so nothing moves when they arrive.
/// </summary>
public static class DrawFirst
{
    /// <summary>Runs <paramref name="wire"/> once the frame now being built has been drawn and pending input is handled.</summary>
    public static void ThenWire(Action wire) => Dispatcher.UIThread.Post(wire, DispatcherPriority.Background);
}
