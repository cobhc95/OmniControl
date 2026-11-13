using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>
/// Wiring only: the editor cards use the shared keyboard model (<see cref="RowEditing"/>); the scale is a layout scale (hit-testing,
/// wrapping and scrolling follow it), and the scroll viewer sits outside it so its bar keeps the app's size.
/// </summary>
public partial class EffectEditorView : UserControl
{
    /// <summary>The effect's scale factor as the layout transform of its cards.</summary>
    public static readonly IValueConverter ToScale = new FuncValueConverter<double, ITransform>(f => new ScaleTransform(f, f));

    public EffectEditorView()
    {
        InitializeComponent();
        RowEditing.Attach(Cards);
    }

    /// <summary>Puts the keyboard on the selected card (the first when none). False when there is no card to take it.</summary>
    public bool FocusCards()
    {
        if (!IsEffectivelyVisible || Cards.ItemCount == 0) return false;
        if (Cards.SelectedIndex < 0) Cards.SelectedIndex = 0;
        (Cards.ContainerFromIndex(Cards.SelectedIndex) as Control ?? Cards).Focus();
        return true;
    }
}
