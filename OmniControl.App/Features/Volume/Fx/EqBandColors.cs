namespace OmniControl.App.Features.Volume.Fx;

/// The one place that says which colour a parametric band has (BrushBand1..6 in Styles/Brushes.axaml, cycling by band place); the graph's circles and the band list's dots both ask here.
internal static class EqBandColors
{
    private const int Count = 6;

    public static IBrush Of(Control control, int index) => DrawKit.Brush(control, $"BrushBand{index % Count + 1}");
}
