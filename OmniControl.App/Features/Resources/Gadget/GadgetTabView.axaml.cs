using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace OmniControl.App.Features.Resources.Gadget;

/// Wiring only: shows a <see cref="GadgetSettingsModel"/>; rows reorder by dragging (<see cref="ListDragReorder"/>) and a pop-down closes after a pick.
public partial class GadgetTabView : UserControl
{
    public GadgetTabView()
    {
        InitializeComponent();
        ListDragReorder.Attach(ReadingRows, (rows, at) => (DataContext as GadgetSettingsModel)?.Move(rows, at));
    }

    private void OnPicked(object? sender, RoutedEventArgs e) => (sender as Control)?.FindLogicalAncestorOfType<Popup>()?.Close();
}
