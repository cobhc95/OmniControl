using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages;

public partial class GalleryRow : UserControl
{
    public GalleryRow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => FitHalf();
    }

    /// <summary>
    /// A half-width card has no room for the page-wide label column: its label takes only its own width (it leaves the shared column
    /// group) and the gallery sits at the right end (the half style in Theme.axaml).
    /// </summary>
    private void FitHalf()
    {
        if (DataContext is not GalleryItem { Half: true }) return;
        var label = Layout.ColumnDefinitions[0];
        label.SharedSizeGroup = null;
        label.MinWidth = 0;
        LabelText.Margin = new Thickness(0, 0, 8, 0);
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(1);

    /// <summary>The wheel over the card steps the choice (up = next), like the arrow buttons.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || !ValueWheel.IsOverSelector(e.Source as Visual)) return;
        item.Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <summary>A click on one of the card's colour dots picks that dot; a click anywhere else on the card just focuses it.</summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control card || DataContext is not GalleryItem item) return;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
        for (var visual = e.Source as Visual; visual is not null && visual != card; visual = visual.GetVisualParent())
        {
            if (visual is not Border { Tag: int index } dot || !dot.Classes.Contains(Swatches.Clickable)) continue;
            card.Focus();
            item.Index = index;
            return;
        }
    }
}
