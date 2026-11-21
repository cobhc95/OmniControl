namespace OmniControl.App.Features.Resources.Gadget;

/// Wiring only: shows a <see cref="GadgetSettingsModel"/>; rows reorder by dragging (<see cref="ListDragReorder"/>); a swatch button's
/// pop-down is given its row (a pop-down's content does not inherit the data context) and closes itself after a pick (SwatchList.IsMenu);
/// a text colour's "Custom colour…" opens THE colour editor at its button.
public partial class GadgetTabView : UserControl
{
    public GadgetTabView()
    {
        InitializeComponent();
        ListDragReorder.Attach(ReadingRows, (rows, at) => (DataContext as GadgetSettingsModel)?.Move(rows, at));
    }

    private void OnSwatchMenuOpened(object? sender, Flyout flyout)
    {
        if (flyout.Content is Control menu) menu.DataContext = (sender as Control)?.DataContext;
    }

    private void OnCustomTextColour(object? sender, Control opener)
    {
        if ((sender as Control)?.DataContext is GadgetRowItem row) SwatchMenu.OpenEditor(opener, _ => new ColourEditor { DataContext = row.CustomTextColour() });
    }
}
