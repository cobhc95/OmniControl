using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniControl.App.Fx;

/// <summary>The small colour dot in a band's list row: the same colour as the band's circle on the graph (<see cref="EqBandColors"/>); its DataContext is the band.</summary>
public sealed class BandDot : Control
{
    private const double Size = 14;
    private EqBandItem? _band;

    public BandDot()
    {
        Width = Height = Size;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null) Watch(DataContext as EqBandItem);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as EqBandItem);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(EqBandItem? band)
    {
        if (ReferenceEquals(_band, band)) return;
        if (_band is not null) _band.PropertyChanged -= OnBandChanged;
        _band = band;
        if (band is not null) band.PropertyChanged += OnBandChanged;
        InvalidateVisual();
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EqBandItem.Index)) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (_band is null) return;
        var r = Size / 2;
        ctx.DrawEllipse(EqBandColors.Of(this, _band.Index), null, new Point(r, r), r, r);
    }
}
