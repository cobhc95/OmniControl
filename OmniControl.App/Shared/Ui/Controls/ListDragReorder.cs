using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Controls;

/// Drag rows of a ListBox up or down to reorder them (the FX window's effect chain, the preset manager's cards). A press on a button,
/// a check box or a text box is not a drag. A press on a selected row of a multi-selection drags the whole selection
/// together (a plain click on it, without dragging, then selects that row alone, as in Windows Explorer and TabForge); a press on
/// any other row drags that row. Nothing is laid out while dragging: the lifted rows follow the pointer as one block and the rows
/// they pass slide aside with a render transform (GPU only, no layout), so it stays smooth. The list is changed once, on release,
/// through <c>moved(rows, insertAt)</c>: the dragged rows (ascending) go, in their order, in front of the <c>insertAt</c>-th of the rows
/// that stay (their count: the end). Esc (<see cref="Drag.Cancel"/>) or losing the pointer puts everything back. Rows are one height.
public static class ListDragReorder
{
    /// How far (DIPs) the pointer must move before a press becomes a drag (also the rubber band's start, <see cref="RubberBandSelect"/>).
    public const double StartDistance = 4;

    /// Lifted rows are drawn over the others.
    private const int LiftedZ = 10;

    public static Drag Attach(ListBox list, Action<IReadOnlyList<int>, int> moved) => new(list, moved);

    public sealed class Drag
    {
        private readonly ListBox _list;
        private readonly Action<IReadOnlyList<int>, int> _moved;
        private Point _press;
        private int _pressRow = -1, _target;
        private List<int> _moving = new();
        private double _pitch;
        private bool _dragging, _selectAloneOnRelease;
        private IPointer? _pointer;

        internal Drag(ListBox list, Action<IReadOnlyList<int>, int> moved)
        {
            _list = list;
            _moved = moved;
            list.AddHandler(InputElement.PointerPressedEvent, OnDown, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerReleasedEvent, OnUp, RoutingStrategies.Tunnel);
            list.PointerCaptureLost += (_, _) => Reset();
        }

        /// Esc: ends a drag without moving anything. False when no drag was going on.
        public bool Cancel()
        {
            if (!_dragging) return false;
            Reset();
            return true;
        }

        private void OnDown(object? sender, PointerPressedEventArgs e)
        {
            _pressRow = -1;
            _selectAloneOnRelease = false;
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return; // clicks on a button or the like are not drags
            if (e.Source is Visual c && (c.FindAncestorOfType<CheckBox>(true) is not null || c.FindAncestorOfType<TextBox>(true) is not null)) return;
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true) is not { } item) return;
            _pressRow = _list.IndexFromContainer(item);
            _press = e.GetPosition(_list);
            _pointer = e.Pointer;
            // A plain press on a row of a multi-selection keeps the selection, so all of it can be dragged; the list would select the row alone.
            if (e.KeyModifiers == KeyModifiers.None && item.IsSelected && (_list.SelectedItems?.Count ?? 0) > 1)
            {
                _selectAloneOnRelease = true;
                item.Focus();
                e.Handled = true;
            }
        }

        private void OnMove(object? sender, PointerEventArgs e)
        {
            if (_pressRow < 0) return;
            var y = e.GetPosition(_list).Y;
            if (!_dragging)
            {
                if (Math.Abs(y - _press.Y) < StartDistance || !Begin()) return;
            }
            Update(y - _press.Y);
            e.Handled = true;
        }

        /// Lifts the selection (when the pressed row is part of it) or the pressed row alone.
        private bool Begin()
        {
            var selected = Enumerable.Range(0, _list.ItemCount).Where(i => _list.ContainerFromIndex(i) is ListBoxItem { IsSelected: true }).ToList();
            _moving = selected.Contains(_pressRow) ? selected : new List<int> { _pressRow };
            _pitch = Pitch();
            if (_moving.Count >= _list.ItemCount || _pitch <= 0) { _pressRow = -1; return false; } // nothing could change place
            _dragging = true;
            _selectAloneOnRelease = false;
            _pointer?.Capture(_list);
            for (var i = 0; i < _list.ItemCount; i++)
            {
                if (_list.ContainerFromIndex(i) is not Control row) continue;
                if (_moving.Contains(i)) row.ZIndex = LiftedZ;
                else row.Transitions = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Timing.DragSlide } };
            }
            return true;
        }

        /// The distance from one row to the next (row height and any gap).
        private double Pitch()
        {
            if (_list.ContainerFromIndex(0) is not Control first) return 0;
            return _list.ContainerFromIndex(1) is Control second ? second.Bounds.Top - first.Bounds.Top : first.Bounds.Height;
        }

        private void Update(double dy)
        {
            var rest = _list.ItemCount - _moving.Count;
            // The lifted block's top, counted from the first row's top, as if the lifted rows had left the list.
            var blockTop = Math.Clamp((_pressRow - _moving.IndexOf(_pressRow)) * _pitch + dy, 0, rest * _pitch);
            _target = Math.Clamp((int)Math.Round(blockTop / _pitch), 0, rest);
            for (var i = 0; i < _list.ItemCount; i++)
            {
                if (_list.ContainerFromIndex(i) is not { } row) continue;
                var lifted = _moving.IndexOf(i);
                if (lifted >= 0) { row.RenderTransform = Shift(blockTop + lifted * _pitch - i * _pitch); continue; }
                var place = i - _moving.Count(m => m < i); // its place among the rows that stay
                var shown = place < _target ? place : place + _moving.Count;
                row.RenderTransform = Shift((shown - i) * _pitch);
            }
        }

        private void OnUp(object? sender, PointerReleasedEventArgs e)
        {
            var drop = _dragging;
            var (rows, at, pressed, alone) = (_moving, _target, _pressRow, _selectAloneOnRelease);
            Reset();
            if (drop) _moved(rows, at);
            else if (alone && pressed >= 0)
            {
                _list.Selection.Clear();
                _list.Selection.Select(pressed);
            }
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
            _selectAloneOnRelease = false;
            _pressRow = -1;
            _moving = new List<int>();
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
