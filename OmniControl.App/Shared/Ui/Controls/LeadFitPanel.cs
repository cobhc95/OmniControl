namespace OmniControl.App.Shared.Ui.Controls;

/// THE row for "a leading chip, then the main control": the first child (a chip) keeps its own width
/// while there is room and gives way FIRST, trimming its words, down to <see cref="LeadMinWidth"/>; only then does the second child (a slider
/// with its value) shrink, down to <see cref="MainMinWidth"/>. A collapsed first child takes no room. Layout only.
public class LeadFitPanel : Panel
{
    public static readonly StyledProperty<double> LeadMinWidthProperty = AvaloniaProperty.Register<LeadFitPanel, double>(nameof(LeadMinWidth));
    public static readonly StyledProperty<double> MainMinWidthProperty = AvaloniaProperty.Register<LeadFitPanel, double>(nameof(MainMinWidth));
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<LeadFitPanel, double>(nameof(Spacing));

    static LeadFitPanel() => AffectsMeasure<LeadFitPanel>(LeadMinWidthProperty, MainMinWidthProperty, SpacingProperty);

    /// The least width the chip is trimmed to.
    public double LeadMinWidth { get => GetValue(LeadMinWidthProperty); set => SetValue(LeadMinWidthProperty, value); }

    /// The least width the main control is shrunk to.
    public double MainMinWidth { get => GetValue(MainMinWidthProperty); set => SetValue(MainMinWidthProperty, value); }

    /// The gap between the two.
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private Control? Lead => Children.Count > 0 && Children[0].IsVisible ? Children[0] : null;
    private Control? Main => Children.Count > 1 ? Children[1] : null;
    private double Gap => Lead is null ? 0 : Spacing;

    protected override Size MeasureOverride(Size available)
    {
        double leadWidth = 0, height = 0;
        if (Lead is { } lead)
        {
            lead.Measure(new Size(double.PositiveInfinity, available.Height));
            var room = Math.Max(LeadMinWidth, available.Width - Gap - MainMinWidth);
            leadWidth = Math.Min(lead.DesiredSize.Width, room);
            lead.Measure(new Size(leadWidth, available.Height));
            leadWidth = lead.DesiredSize.Width;
            height = lead.DesiredSize.Height;
        }
        double mainWidth = 0;
        if (Main is { } main)
        {
            main.Measure(new Size(Math.Max(MainMinWidth, available.Width - leadWidth - Gap), available.Height));
            mainWidth = main.DesiredSize.Width;
            height = Math.Max(height, main.DesiredSize.Height);
        }
        return new Size(leadWidth + Gap + mainWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        if (Lead is { } lead)
        {
            var w = Math.Min(lead.DesiredSize.Width, finalSize.Width);
            lead.Arrange(new Rect(0, 0, w, finalSize.Height));
            x = w + Gap;
        }
        Main?.Arrange(new Rect(x, 0, Math.Max(MainMinWidth, finalSize.Width - x), finalSize.Height));
        return finalSize;
    }
}
