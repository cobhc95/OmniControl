using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace OmniControl.App.Shared.Ui.Pages;

/// A layout built only while shown, by its <c>IsVisible</c>: one of a card's layouts (full or half width, slider or knob), so a card builds
/// only the layout its item uses, and a group's body (<see cref="GroupCard"/>), built when the group opens. Its <see cref="Layout"/> is
/// built the first time it is laid out, never while hidden, and let go when it is hidden. Layout only.
public sealed class CardLayout : Control
{
    public static readonly StyledProperty<IDataTemplate?> LayoutProperty = AvaloniaProperty.Register<CardLayout, IDataTemplate?>(nameof(Layout));

    private Control? _built;

    /// The layout, as a <c>DataTemplate</c> written inside the element; it reads the inherited data context (the card's item, the page's model).
    [Content]
    public IDataTemplate? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == IsVisibleProperty && !IsVisible) || change.Property == LayoutProperty) LetGo();
    }

    private void LetGo()
    {
        if (_built is null) return;
        LogicalChildren.Remove(_built);
        VisualChildren.Remove(_built);
        _built = null;
        InvalidateMeasure();
    }

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
