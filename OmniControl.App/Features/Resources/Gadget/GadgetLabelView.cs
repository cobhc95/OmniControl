using Avalonia.Layout;

namespace OmniControl.App.Features.Resources.Gadget;

/// THE drawing of a gadget label (<see cref="GadgetLabel"/>): its words and icons side by side at <see cref="TextSize"/> (NaN: the theme's size), a
/// coloured icon in its own colour unless <see cref="Warned"/>.
public sealed class GadgetLabelView : StackPanel
{
    private const double IconScale = 1.15;

    /// The room between a gadget's parts (a label's words and icons, a label and its value), in text sizes.
    internal const double PartGap = 0.25;

    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<GadgetLabelView, string?>(nameof(Label));
    public static readonly StyledProperty<double> TextSizeProperty = AvaloniaProperty.Register<GadgetLabelView, double>(nameof(TextSize), double.NaN);

    /// The reading is past its warn point: every icon takes the cell's warn colour.
    public static readonly StyledProperty<bool> WarnedProperty = AvaloniaProperty.Register<GadgetLabelView, bool>(nameof(Warned));

    public GadgetLabelView() => (Orientation, VerticalAlignment) = (Orientation.Horizontal, VerticalAlignment.Center);

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public double TextSize { get => GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }

    public bool Warned { get => GetValue(WarnedProperty); set => SetValue(WarnedProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty || change.Property == TextSizeProperty || change.Property == WarnedProperty) Build();
    }

    private void Build()
    {
        Children.Clear();
        var size = double.IsNaN(TextSize) ? (double?)null : TextSize;
        if (size is { } s) Spacing = s * PartGap;
        foreach (var part in GadgetLabel.Parts(Label ?? string.Empty))
        {
            if (part.Icon is { } icon)
            {
                var glyph = new IconGlyph { LibraryId = icon };
                if (size is { } px) (glyph.Width, glyph.Height) = (px * IconScale, px * IconScale);
                if (!Warned && part.Ink is { } ink) glyph.Foreground = new SolidColorBrush(ink);
                Children.Add(glyph);
                continue;
            }
            var words = new TextBlock { Text = part.Text, VerticalAlignment = VerticalAlignment.Center };
            if (size is { } fs) words.FontSize = fs;
            Children.Add(words);
        }
        IsVisible = Children.Count > 0;
    }
}
