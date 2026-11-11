using OmniControl.App.Services;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace OmniControl.App.Fx;

/// <summary>
/// Drag a row of a ListBox up or down to reorder it. Nothing is laid out while dragging: the lifted row follows the pointer and the rows it passes
/// slide aside with a render transform (GPU only, no layout), so it stays smooth. The list is changed once, on release, through <c>moved(from, to)</c>.
/// Esc, or losing the pointer, puts everything back. Rows are expected to be one height.
/// </summary>
public static class ListDragReorder
{
    private const double StartDistance = 4;
    private static readonly TimeSpan Slide = Timing.DragSlide;

    public static void Attach(ListBox list, Action<int, int> moved) => _ = new Session(list, moved);

    private sealed class Session
    {
        private readonly ListBox _list;
        private readonly Action<int, int> _moved;
        private Point _press;
        private int _from = -1, _target;
        private bool _dragging;
        private IPointer? _pointer;

        public Session(ListBox list, Action<int, int> moved)
        {
            _list = list;
            _moved = moved;
            list.AddHandler(InputElement.PointerPressedEvent, OnDown, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerReleasedEvent, OnUp, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            list.PointerCaptureLost += (_, _) => Reset();
        }

        private void OnDown(object? sender, PointerPressedEventArgs e)
        {
            _from = -1;
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return; // clicks on a button or the like are not drags
            if (e.Source is Visual c && (c is CheckBox || c.FindAncestorOfType<CheckBox>(true) is not null)) return;
            var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true);
            if (item is null) return;
            _from = _list.IndexFromContainer(item);
            _press = e.GetPosition(_list);
            _pointer = e.Pointer;
        }

        private void OnMove(object? sender, PointerEventArgs e)
        {
            if (_from < 0) return;
            var y = e.GetPosition(_list).Y;
            if (!_dragging)
            {
                if (Math.Abs(y - _press.Y) < StartDistance || _list.ItemCount < 2) return;
                _dragging = true;
                _pointer?.Capture(_list);
                if (_list.ContainerFromIndex(_from) is { } lifted) lifted.ZIndex = 10;
                for (var i = 0; i < _list.ItemCount; i++)
                    if (i != _from && _list.ContainerFromIndex(i) is Control other)
                        other.Transitions = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Slide } };
            }
            Update(y - _press.Y);
            e.Handled = true;
        }

        private void Update(double dy)
        {
            if (_list.ContainerFromIndex(_from) is not { } lifted) return;
            var h = lifted.Bounds.Height;
            if (h <= 0) return;
            dy = Math.Clamp(dy, -_from * h, (_list.ItemCount - 1 - _from) * h);
            lifted.RenderTransform = Shift(dy);
            _target = Math.Clamp((int)Math.Round(_from + dy / h), 0, _list.ItemCount - 1);

            for (var i = 0; i < _list.ItemCount; i++)
            {
                if (i == _from || _list.ContainerFromIndex(i) is not { } row) continue;
                var offset = _from < _target && i > _from && i <= _target ? -h
                           : _target < _from && i >= _target && i < _from ? h
                           : 0;
                row.RenderTransform = Shift(offset);
            }
        }

        private void OnUp(object? sender, PointerReleasedEventArgs e)
        {
            var drop = _dragging && _from >= 0 && _target != _from;
            var (from, to) = (_from, _target);
            Reset();
            if (drop) _moved(from, to);
        }

        private void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _dragging) { Reset(); e.Handled = true; }
        }

        private void Reset()
        {
            for (var i = 0; i < _list.ItemCount; i++)
                if (_list.ContainerFromIndex(i) is { } row)
                {
                    row.Transitions = null;
                    row.RenderTransform = null;
                    row.ZIndex = 0;
                }
            if (_dragging) _pointer?.Capture(null);
            _dragging = false;
            _from = -1;
            _pointer = null;
        }

        private static ITransform Shift(double y)
        {
            var b = new TransformOperations.Builder(1);
            b.AppendTranslate(0, y);
            return b.Build();
        }
    }
}
