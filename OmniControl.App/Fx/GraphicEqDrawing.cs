namespace OmniControl.App.Fx;

/// <summary>
/// The shared part of a graphic equaliser's drawn pieces (<see cref="GraphicEqCurve"/>, <see cref="GainScale"/>,
/// <see cref="BandLabels"/>): they follow the <see cref="GraphicEqItem"/> given as DataContext (<see cref="ItemDrawing{T}"/>) and
/// draw its bands in equal columns.
/// </summary>
public abstract class GraphicEqDrawing : ItemDrawing<GraphicEqItem>
{
    /// <summary>The bands are drawn in equal columns across the width, in order, like the sliders' columns under or beside them.</summary>
    protected double ColumnWidth => Item is { Bands.Count: > 0 } item ? Bounds.Width / item.Bands.Count : Bounds.Width;
}
