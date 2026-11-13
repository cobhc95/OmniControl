using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the band slider that gets the keyboard becomes the equaliser's selected band (the curve reads it out).</summary>
public partial class GraphicEqRow : UserControl
{
    public GraphicEqRow()
    {
        InitializeComponent();
        BandList.AddHandler(GotFocusEvent, OnBandFocused, RoutingStrategies.Bubble);
    }

    private void OnBandFocused(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is GraphicEqItem eq && e.Source is Control { DataContext: GraphicBandItem band }) eq.Selected = band;
    }
}
