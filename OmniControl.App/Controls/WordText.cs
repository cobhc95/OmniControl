using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniControl.App.Controls;

/// <summary>
/// A wrapping name that breaks only between words (a long word is never split as "Landscap/e"): when the longest word does not fit the width
/// the text drops to <see cref="FitFontSize"/> (one step smaller), and when it still does not fit it stays on one line with an ellipsis.
/// Takes the look of a plain <see cref="TextBlock"/>, so every TextBlock style applies.
/// </summary>
public sealed class WordText : TextBlock
{
    public static readonly StyledProperty<double> FitFontSizeProperty = AvaloniaProperty.Register<WordText, double>(nameof(FitFontSize), 9);

    private double? _fullSize;

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The one smaller size tried when the longest word is wider than the text's width.</summary>
    public double FitFontSize { get => GetValue(FitFontSizeProperty); set => SetValue(FitFontSizeProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width) && !string.IsNullOrEmpty(Text))
        {
            _fullSize ??= FontSize;
            var full = _fullSize.Value;
            var size = Widest(full) <= availableSize.Width ? full : Math.Min(full, FitFontSize);
            if (size != FontSize) SetCurrentValue(FontSizeProperty, size);
            SetCurrentValue(TextWrappingProperty, Widest(size) <= availableSize.Width ? TextWrapping.Wrap : TextWrapping.NoWrap);
        }
        return base.MeasureOverride(availableSize);
    }

    private double Widest(double size)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        return Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection, typeface, size, null).Width)
            .DefaultIfEmpty(0).Max();
    }
}
