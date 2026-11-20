using Avalonia.Media;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE fill that grows from the left as something runs (a counting pill): <see cref="Fraction"/> of its width in <see cref="Fill"/>, inside its rounded corners.
public sealed class ProgressFill : Control
{
    public static readonly StyledProperty<double> FractionProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(Fraction));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ProgressFill, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<ProgressFill, double>(nameof(Radius));

    static ProgressFill() => AffectsRender<ProgressFill>(FractionProperty, FillProperty, RadiusProperty);

    public ProgressFill() => IsHitTestVisible = false;

    /// How much is filled, 0 to 1.
    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// The corner radius of the shape it fills.
    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width * Math.Clamp(Fraction, 0, 1);
        if (Fill is null || width <= 0) return;
        using (context.PushClip(new RoundedRect(new Rect(Bounds.Size), Radius)))
            context.FillRectangle(Fill, new Rect(0, 0, width, Bounds.Height));
    }
}
