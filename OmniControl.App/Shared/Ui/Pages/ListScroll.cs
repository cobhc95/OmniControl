using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE way to scroll a list's line into view. Avalonia's virtualizing panel, asked to scroll before it has measured rows that were just
/// removed or inserted, runs its own layout pass that skips the measure and arranges at an unknown (NaN) start: "Invalid Arrange rectangle".
/// So the list's layout is settled first, and a scroll asked for while the rows change waits until the list has laid them out.
public static class ListScroll
{
    /// Scrolls to line <paramref name="index"/> now (a virtualizing list builds its row) and gives its row; null when there is no such
    /// line or the list is not on screen.
    public static Control? Show(ListBox list, int index)
    {
        if (index < 0 || index >= list.ItemCount || list.GetVisualRoot() is null) return null;
        list.UpdateLayout();
        list.ScrollIntoView(index);
        return list.ContainerFromIndex(index);
    }

    /// Scrolls to the line <paramref name="index"/> gives once the list has laid out its changed rows (read then, so the newest wins).
    public static void ShowSoon(ListBox list, Func<int> index) =>
        Dispatcher.UIThread.Post(() => Guard.Run("Scroll a list", () => Show(list, index())), DispatcherPriority.Loaded);
}
