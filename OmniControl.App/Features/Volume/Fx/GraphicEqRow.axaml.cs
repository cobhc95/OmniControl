using Avalonia.Input;

namespace OmniControl.App.Features.Volume.Fx;

/// Wiring only: the band slider that gets the keyboard becomes the equaliser's selected band (the curve reads it out).
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
