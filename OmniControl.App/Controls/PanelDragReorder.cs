using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>
/// Drag one item of an ItemsControl to another place, smooth like a browser's tabs: a grid of tiles (both directions) or a list of rows
/// (up and down only). A press becomes a drag once the pointer has moved <see cref="ListDragReorder.StartDistance"/> with the button held;
/// a click without moving stays a click (a tile still runs). Nothing is laid out while dragging: the dragged item follows the pointer with a
/// render transform (each frame the pointer moves) and the items it passes slide aside with a short eased transition; the slot maths are
/// <see cref="Reorder.TargetSlot"/> and <see cref="Reorder.ShownSlot"/>. The list is changed once, on release, through
/// <c>moved(from, to)</c> (the item's index before and after); Esc (<see cref="Cancel"/>) or losing the pointer puts everything back.
/// A press on a slider, a pick-list chip or a small glyph button (a remove cross) is never a drag.
/// </summary>
public sealed class PanelDragReorder
{
    /// <summary>The dragged item is drawn over the others.</summary>
    private const int LiftedZ = 10;

    private readonly ItemsControl _items;
    private readonly bool _vertical;
    private readonly Func<bool> _enabled;
    private readonly Func<object?, bool> _canDrag;
    private readonly Action<int, int> _moved;
    private Point _press;
    private int _from = -1, _to = -1;
    private bool _dragging;
    private IPointer? _pointer;
    private List<Rect> _slots = new();

    /// <param name="vertical">A list of rows: the item moves up and down only.</param>
    /// <param name="enabled">False while the layout is locked: no drag starts.</param>
    /// <param name="canDrag">Whether an item may be dragged (the dashed "+ Add" tile may not); every other item may still move aside.</param>
    /// <param name="moved">The item at the first index was dropped at the second (both among the draggable items, in their order).</param>
    public PanelDragReorder(ItemsControl items, bool vertical, Func<bool> enabled, Func<object?, bool> canDrag, Action<int, int> moved)
    {
        (_items, _vertical, _enabled, _canDrag, _moved) = (items, vertical, enabled, canDrag, moved);
        items.AddHandler(InputElement.PointerPressedEvent, OnDown, RoutingStrategies.Tunnel);
        items.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
        items.AddHandler(InputElement.PointerReleasedEvent, OnUp, RoutingStrategies.Tunnel);
        items.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, items)) Reset(); // the tile handing its capture over at the start is not a loss
        };
    }

    /// <summary>Esc: ends a drag without moving anything. False when no drag was going on.</summary>
    public bool Cancel()
    {
        if (!_dragging) return false;
        Reset();
        return true;
    }

    private int Count => Enumerable.Range(0, _items.ItemCount).Count(i => _canDrag(_items.Items[i]));

    private void OnDown(object? sender, PointerPressedEventArgs e)
    {
        _from = -1;
        if (!e.GetCurrentPoint(_items).Properties.IsLeftButtonPressed || !_enabled()) return;
        if (e.Source is not Visual source || IsControlPart(source)) return;
        var container = source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.GetVisualParent() is Panel panel && panel == _items.ItemsPanelRoot);
        if (container is null) return;
        var index = _items.IndexFromContainer(container);
        if (index < 0 || !_canDrag(_items.Items[index])) return;
        _from = index;
        _press = e.GetPosition(_items);
        _pointer = e.Pointer;
    }

    private static bool IsControlPart(Visual source) =>
        source.GetSelfAndVisualAncestors().Any(v => v is Slider or ChipMenu || v is Button button && button.Classes.Contains("glyph"));

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (_from < 0) return;
        Vector delta = e.GetPosition(_items) - _press;
        if (_vertical) delta = new Vector(0, delta.Y);
        if (!_dragging)
        {
            if (Math.Abs(delta.X) < ListDragReorder.StartDistance && Math.Abs(delta.Y) < ListDragReorder.StartDistance) return;
            if (!Begin()) return;
        }
        Update(delta);
        e.Handled = true;
    }

    private bool Begin()
    {
        var count = Count;
        if (count < 2) { _from = -1; return false; }
        _slots = Enumerable.Range(0, count).Select(i => _items.ContainerFromIndex(i)?.Bounds ?? default).ToList();
        _dragging = true;
        _to = _from;
        _pointer?.Capture(_items); // the tile under the press lets go of it, so the drop is not also a click
        for (var i = 0; i < count; i++)
        {
            if (_items.ContainerFromIndex(i) is not Control item) continue;
            if (i == _from) item.ZIndex = LiftedZ;
            else item.Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Timing.DragSlide, Easing = new CubicEaseOut() },
            };
        }
        return true;
    }

    private void Update(Vector delta)
    {
        _to = Reorder.TargetSlot(_slots, _slots[_from].Center + delta);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_items.ContainerFromIndex(i) is not Control item) continue;
            item.RenderTransform = i == _from ? Shift(delta) : Shift((Vector)(_slots[Reorder.ShownSlot(i, _from, _to)].TopLeft - _slots[i].TopLeft));
        }
    }

    private void OnUp(object? sender, PointerReleasedEventArgs e)
    {
        var (drop, from, to) = (_dragging, _from, _to);
        Reset();
        if (!drop) return;
        e.Handled = true;
        if (to >= 0 && to != from) _moved(from, to);
    }

    private void Reset()
    {
        for (var i = 0; i < _items.ItemCount; i++)
            if (_items.ContainerFromIndex(i) is { } item)
            {
                item.Transitions = null;
                item.RenderTransform = null;
                item.ZIndex = 0;
            }
        if (_dragging) _pointer?.Capture(null);
        _dragging = false;
        _from = _to = -1;
        _pointer = null;
        _slots = new List<Rect>();
    }

    private static ITransform Shift(Vector by)
    {
        var b = new TransformOperations.Builder(1);
        b.AppendTranslate(by.X, by.Y);
        return b.Build();
    }
}
