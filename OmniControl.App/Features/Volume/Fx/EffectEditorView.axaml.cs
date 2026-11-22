using Avalonia.Data.Converters;

namespace OmniControl.App.Features.Volume.Fx;

/// Wiring only: the editor cards use the shared keyboard model (<see cref="RowEditing"/>); the scale is a layout scale (hit-testing,
/// wrapping and scrolling follow it), and the scroll viewer sits outside it so its bar keeps the app's size.
public partial class EffectEditorView : UserControl
{
    /// The effect's scale factor as the layout transform of its cards.
    public static readonly IValueConverter ToScale = new FuncValueConverter<double, ITransform>(f => new ScaleTransform(f, f));

    public EffectEditorView()
    {
        InitializeComponent();
        RowEditing.Attach(Cards);
    }

    /// Puts the keyboard on the selected card (the first when none). False when there is no card to take it.
    public bool FocusCards()
    {
        if (!IsEffectivelyVisible || Cards.ItemCount == 0) return false;
        if (Cards.SelectedIndex < 0) Cards.SelectedIndex = 0;
        (ListScroll.Show(Cards, Cards.SelectedIndex) ?? Cards).Focus(); // a long list builds the card first when it is out of view
        return true;
    }
}
