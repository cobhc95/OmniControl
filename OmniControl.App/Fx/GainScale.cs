using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniControl.App.Fx;

/// <summary>
/// The dB scale at the left of a graphic equaliser's sliders: the range's ends, halves and 0 dB, each beside the place a slider's
/// knob has at that gain (the knob travels the slider's height less its own height, <c>VBandThumbHeight</c>).
/// </summary>
public sealed class GainScale : GraphicEqDrawing
{
    /// <summary>Space between the labels and the sliders.</summary>
    private const double Gap = 6;

    private const string ThumbHeightKey = "VBandThumbHeight";

    private static readonly double[] Marks = { 1, 0.5, 0, -0.5, -1 };

    protected override Size MeasureOverride(Size availableSize)
    {
        var widest = Item is { } item ? Marks.Max(m => Label(m * item.RangeDb).Width) : 0;
        return new Size(widest + Gap, 0);
    }

    /// <summary>The range may have changed, and with it the widest label.</summary>
    protected override void OnItemChanged()
    {
        InvalidateMeasure();
        base.OnItemChanged();
    }

    public override void Render(DrawingContext ctx)
    {
        if (Item is not { } item) return;
        var thumb = this.TryFindResource(ThumbHeightKey, out var v) && v is double h ? h : 0;
        var travel = Math.Max(1, Bounds.Height - thumb);
        foreach (var mark in Marks)
        {
            var text = Label(mark * item.RangeDb);
            var y = thumb / 2 + travel * (1 - mark) / 2;
            ctx.DrawText(text, new Point(Bounds.Width - Gap - text.Width, Math.Clamp(y - text.Height / 2, 0, Math.Max(0, Bounds.Height - text.Height))));
        }
    }

    private FormattedText Label(double db) => DrawKit.Text(db > 0 ? $"+{db:0}" : $"{db:0}", DrawKit.LabelSize, DrawKit.Brush(this, "BrushMuted"));
}
