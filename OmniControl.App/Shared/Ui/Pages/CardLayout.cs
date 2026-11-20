using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace OmniControl.App.Shared.Ui.Pages;

/// One of a card's layouts (full or half width, slider or knob), shown by its <c>IsVisible</c>: its <see cref="Layout"/> is built the first
/// time it is laid out, never while hidden, so a card builds only the layout its item uses instead of all of them. Layout only.
public sealed class CardLayout : Control
{
    private Control? _built;

    /// The layout, as a <c>DataTemplate</c> written inside the element; it reads the card's item (the inherited data context).
    [Content]
    public IDataTemplate? Layout { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_built is null && Layout?.Build(DataContext) is { } built)
        {
            _built = built;
            LogicalChildren.Add(built);
            VisualChildren.Add(built);
        }
        _built?.Measure(availableSize);
        return _built?.DesiredSize ?? default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _built?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
