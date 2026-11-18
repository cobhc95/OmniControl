using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Controls;

/// <summary>
/// THE row of a half-width card: the label (first child) beside the main control (second child: a <see cref="LeadFitPanel"/> with the slider and its value).
/// Side by side, the label takes <see cref="LabelShare"/> of the width (never below <see cref="LabelMinWidth"/>) and the main control the rest, never below
/// <see cref="MainMinWidth"/>. When even those two minimums do not fit, the main control goes under the label inside the same card (the card grows
/// taller), so nothing is hidden or clipped at any width. Layout only.
/// </summary>
public sealed class LabelFitPanel : Panel
{
    public static readonly StyledProperty<double> LabelMinWidthProperty = AvaloniaProperty.Register<LabelFitPanel, double>(nameof(LabelMinWidth));
    public static readonly StyledProperty<double> MainMinWidthProperty = AvaloniaProperty.Register<LabelFitPanel, double>(nameof(MainMinWidth));
    public static readonly StyledProperty<double> LabelShareProperty = AvaloniaProperty.Register<LabelFitPanel, double>(nameof(LabelShare), 0.4);
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<LabelFitPanel, double>(nameof(Spacing));
    public static readonly StyledProperty<bool> LabelFirstProperty = AvaloniaProperty.Register<LabelFitPanel, bool>(nameof(LabelFirst));

    static LabelFitPanel() => AffectsMeasure<LabelFitPanel>(LabelMinWidthProperty, MainMinWidthProperty, LabelShareProperty, SpacingProperty, LabelFirstProperty);

    /// <summary>True: the label keeps its whole one-line width (never wrapped or broken) and the main control gets what is left, trimming its words first; the share is ignored.</summary>
    public bool LabelFirst { get => GetValue(LabelFirstProperty); set => SetValue(LabelFirstProperty, value); }

    public double LabelMinWidth { get => GetValue(LabelMinWidthProperty); set => SetValue(LabelMinWidthProperty, value); }

    public double MainMinWidth { get => GetValue(MainMinWidthProperty); set => SetValue(MainMinWidthProperty, value); }

    /// <summary>The label's share of the width when side by side (0 to 1).</summary>
    public double LabelShare { get => GetValue(LabelShareProperty); set => SetValue(LabelShareProperty, value); }

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private Control? Label => Children.Count > 0 ? Children[0] : null;
    private Control? Main => Children.Count > 1 ? Children[1] : null;

    private bool _stacked;
    private double _labelWidth;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? LabelMinWidth + Spacing + MainMinWidth : available.Width;
        _stacked = width < LabelMinWidth + Spacing + MainMinWidth;
        _labelWidth = _stacked ? width : LabelFirst ? NaturalLabelWidth(width) : Math.Clamp((width - Spacing) * LabelShare, LabelMinWidth, width - Spacing - MainMinWidth);
        var mainWidth = _stacked ? width : width - Spacing - _labelWidth;
        Label?.Measure(new Size(_labelWidth, double.PositiveInfinity));
        Main?.Measure(new Size(mainWidth, double.PositiveInfinity));
        double labelHeight = Label?.DesiredSize.Height ?? 0, mainHeight = Main?.DesiredSize.Height ?? 0;
        return new Size(width, _stacked ? labelHeight + mainHeight : Math.Max(labelHeight, mainHeight));
    }

    private double NaturalLabelWidth(double width)
    {
        Label?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Clamp(Math.Ceiling(Label?.DesiredSize.Width ?? LabelMinWidth) + 1, LabelMinWidth, width - Spacing - MainMinWidth);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_stacked)
        {
            var labelHeight = Label?.DesiredSize.Height ?? 0;
            Label?.Arrange(new Rect(0, 0, finalSize.Width, labelHeight));
            Main?.Arrange(new Rect(0, labelHeight, finalSize.Width, Main.DesiredSize.Height));
        }
        else
        {
            Label?.Arrange(new Rect(0, 0, _labelWidth, finalSize.Height));
            Main?.Arrange(new Rect(_labelWidth + Spacing, 0, Math.Max(0, finalSize.Width - _labelWidth - Spacing), finalSize.Height));
        }
        return finalSize;
    }
}
