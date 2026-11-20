using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

public partial class GalleryRow : UserControl
{
    private GalleryItem? _hooked;

    public GalleryRow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            FitHalf();
            Hook(VisualRoot is not null);
        };
    }

    /// A half-width card has no room for the page-wide label column: its label takes only its own width (it leaves the shared column
    /// group) and the gallery sits at the right end (the half style in Theme.axaml).
    private void FitHalf()
    {
        if (DataContext is not GalleryItem { Half: true }) return;
        var label = Layout.ColumnDefinitions[0];
        label.SharedSizeGroup = null;
        label.MinWidth = 0;
        LabelText.Margin = new Thickness(0, 0, 8, 0);
    }

    // The item's editor request is heard only while this row is on screen and shows that item.
    private void Hook(bool onScreen)
    {
        var item = onScreen ? DataContext as GalleryItem : null;
        if (item == _hooked) return;
        if (_hooked is not null) _hooked.EditorRequested -= OpenEditor;
        _hooked = item;
        if (_hooked is not null) _hooked.EditorRequested += OpenEditor;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(onScreen: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hook(onScreen: false);
    }

    /// The option's editor in the master card pop-down (ListMenuFlyout, optionmenu) under the gallery, made fresh each time.
    private void OpenEditor()
    {
        if (_hooked?.Editor is not { } make) return;
        var flyout = new ListMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.FlyoutPresenterClasses.Add("optionmenu");
        flyout.Content = make(flyout.Hide);
        flyout.ShowAt(Gallery);
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(1);

    /// The wheel over the card steps the choice (up = next), like the arrow buttons.
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || !ValueWheel.IsOverSelector(e.Source as Visual)) return;
        item.Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// A click on one of the card's colour dots picks that dot (and opens its editor when it has one); a click anywhere else on the card just focuses it.
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control card || DataContext is not GalleryItem item) return;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
        for (var visual = e.Source as Visual; visual is not null && visual != card; visual = visual.GetVisualParent())
        {
            if (visual is not Border { Tag: int index } dot || !dot.Classes.Contains(Swatches.Clickable)) continue;
            card.Focus();
            item.Index = index;
            item.OpenEditor();
            return;
        }
    }
}
