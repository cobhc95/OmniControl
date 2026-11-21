using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only. A picture gallery: the arrows, a click on a picture and the wheel step or pick. A colour gallery: each swatch button's
/// pop-down is filled with the dots when it opens and closes after a pick; its "Custom colour…" row (or Down past the dots, then Enter)
/// opens the item's editor at the button; the wheel over the button steps the choice as it steps a drop-down.
public partial class GalleryRow : UserControl
{
    private Flyout? _open;

    public GalleryRow() => InitializeComponent();

    /// A swatch button's pop-down opens: THE colour pop-down, its editor row shown only for a gallery with an editor.
    private void OnDotsOpened(object? sender, Flyout flyout)
    {
        _open = flyout;
        if (flyout.Content is not SwatchMenu menu || DataContext is not GalleryItem item) return;
        menu.ItemsSource = item.DotItems();
        menu.EditorText = item.Editor is not null ? item.EditorText : null;
    }

    private void OnDotsClosed(object? sender, EventArgs e) => _open = null;

    /// The option's editor in the master card pop-down (ListMenuFlyout, optionmenu) at the swatch button, made fresh each time.
    private void OnEditorClicked(object? sender, Control opener)
    {
        if (DataContext is not GalleryItem { Editor: { } make } item) return;
        item.PickEditorOption();
        SwatchMenu.OpenEditor(opener, make);
    }

    /// A half-width picture gallery has no room for the page-wide label column: its label takes only its own width (it leaves the shared
    /// column group) and the gallery sits at the right end (the half style in Theme.axaml). Done as the layout is built, before it is measured.
    private void OnPictureAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not GalleryItem { Half: true } || sender is not Grid { ColumnDefinitions: [var label, ..], Children: [TextBlock text, ..] }) return;
        label.SharedSizeGroup = null;
        label.MinWidth = 0;
        text.Margin = new Thickness(0, 0, 8, 0);
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(1);

    /// The wheel over a picture gallery steps the choice (up = next), like the arrow buttons.
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || !ValueWheel.IsOverSelector(e.Source as Visual)) return;
        item.Step(ValueWheel.Direction(e));
        e.Handled = true;
    }

    /// The wheel over a closed swatch button steps the choice as it steps a closed drop-down (up = the one before).
    private void OnChipWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || _open is not null) return;
        item.Step(ValueWheel.ListDirection(e));
        e.Handled = true;
    }

    /// A click on one of a picture gallery's pictures picks it; a click anywhere else on the card just focuses it.
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control card || DataContext is not GalleryItem item) return;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
        for (var visual = e.Source as Visual; visual is not null && visual != card; visual = visual.GetVisualParent())
        {
            if (visual is not Border { Tag: int index } picture || !picture.Classes.Contains(GalleryStrip.Clickable)) continue;
            card.Focus();
            item.Index = index;
            return;
        }
    }
}
