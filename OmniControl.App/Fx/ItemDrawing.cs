using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Fx;

/// <summary>An item whose drawn view must draw again when something it shows changes (an equaliser card, the FX window's analysis).</summary>
public interface IRedrawSource
{
    event Action? Redraw;
}

/// <summary>
/// The shared part of every drawn control in the FX window (the equalisers' graphs, curves, scales and labels, the total response):
/// it follows the item it is given as DataContext while it is on screen and draws again when the item raises
/// <see cref="IRedrawSource.Redraw"/> or the theme changes. Drawing only; every value lives in the item.
/// </summary>
public abstract class ItemDrawing<T> : Control where T : class, IRedrawSource
{
    protected ItemDrawing()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected T? Item { get; private set; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null) Watch(DataContext as T); // off screen it waits for OnAttachedToVisualTree, so nothing holds it there
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as T);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(T? item)
    {
        if (ReferenceEquals(Item, item)) return;
        if (Item is not null) Item.Redraw -= OnItemChanged;
        Item = item;
        if (item is not null) item.Redraw += OnItemChanged;
        OnItemChanged();
    }

    /// <summary>Something the item shows changed (or a new item came): draw again.</summary>
    protected virtual void OnItemChanged() => InvalidateVisual();
}
