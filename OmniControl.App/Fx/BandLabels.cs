using Avalonia;
using Avalonia.Media;

namespace OmniControl.App.Fx;

/// <summary>
/// The short frequency labels under a graphic equaliser's sliders ("31.5", "1k", "16k"), each centred under its column. When the
/// columns get too narrow for every label, only every second (or fourth...) one is drawn, so labels never touch; the band last changed
/// is drawn in the accent.
/// </summary>
public sealed class BandLabels : GraphicEqDrawing
{
    /// <summary>Least space between two drawn labels, and above them.</summary>
    private const double Gap = 4;

    protected override Size MeasureOverride(Size availableSize) =>
        new(0, DrawKit.Text("0", DrawKit.LabelSize, Brushes.Transparent).Height + Gap);

    public override void Render(DrawingContext ctx)
    {
        if (Item is not { Bands.Count: > 0 } item) return;
        var muted = DrawKit.Brush(this, "BrushMuted");
        var accent = DrawKit.Brush(this, "BrushAccent");
        var texts = item.Bands.Select(b => DrawKit.Text(b.ShortLabel, DrawKit.LabelSize, ReferenceEquals(b, item.Selected) ? accent : muted)).ToList();
        var every = Math.Max(1, (int)Math.Ceiling((texts.Max(t => t.Width) + Gap) / Math.Max(1, ColumnWidth)));
        for (var i = 0; i < texts.Count; i += every)
        {
            var x = ColumnWidth * (i + 0.5) - texts[i].Width / 2;
            ctx.DrawText(texts[i], new Point(Math.Clamp(x, 0, Math.Max(0, Bounds.Width - texts[i].Width)), Gap));
        }
    }
}
