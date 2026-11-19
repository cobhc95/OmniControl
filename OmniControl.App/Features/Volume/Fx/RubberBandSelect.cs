using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Volume.Fx;

/// Mouse only: press on the empty part of a multi-select ListBox and drag to draw a selection rectangle; every row it touches is selected.
/// Hold Ctrl to add to the current selection. A plain click on the empty part clears the selection; Esc (<see cref="Band.Cancel"/>)
/// while drawing puts back the selection from before the press (as TabForge's FX list does).
/// The rectangle is drawn by <c>band</c>, a Border that sits over the list (not hit-testable) and is moved and sized here.
public static class RubberBandSelect
{
    public static Band Attach(ListBox list, Border band) => new(list, band);

    public sealed class Band
    {
        private readonly ListBox _list;
        private readonly Border _band;
        private Point _start;
        private bool _pressed, _drawing, _additive;
        private List<object> _before = new(), _original = new();
        private IPointer? _pointer;

        internal Band(ListBox list, Border band)
        {
            _list = list;
            _band = band;
            band.HorizontalAlignment = HorizontalAlignment.Left;
            band.VerticalAlignment = VerticalAlignment.Top;
            band.IsHitTestVisible = false;
            band.IsVisible = false;
            list.AddHandler(InputElement.PointerPressedEvent, OnDown, RoutingStrategies.Bubble, true);
            list.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel);
            list.AddHandler(InputElement.PointerReleasedEvent, OnUp, RoutingStrategies.Tunnel);
            list.PointerCaptureLost += (_, _) => Stop();
        }

        private void OnDown(object? sender, PointerPressedEventArgs e)
        {
            _pressed = false;
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
            var src = e.Source as Visual;
            if (src is null || src.FindAncestorOfType<ListBoxItem>(true) is not null) return; // on a row: that is a click or a row drag
            if (src.FindAncestorOfType<ScrollBar>(true) is not null) return;
            _pressed = true;
            _additive = (e.KeyModifiers & KeyModifiers.Control) != 0;
            _start = e.GetPosition(_list);
            _original = _list.SelectedItems?.Cast<object>().ToList() ?? new();
            _before = _additive ? _original.ToList() : new();
            if (!_additive) _list.SelectedItems?.Clear();
            _pointer = e.Pointer;
            e.Pointer.Capture(_list);
        }

        /// Esc: ends the rectangle and puts the selection back as it was before the press. False when none was being drawn.
        public bool Cancel()
        {
            if (!_pressed) return false;
            var original = _original;
            var pointer = _pointer;
            Stop();
            pointer?.Capture(null);
            if (_list.SelectedItems is { } selected)
            {
                selected.Clear();
                foreach (var item in original) selected.Add(item);
            }
            return true;
        }

        private void OnMove(object? sender, PointerEventArgs e)
        {
            if (!_pressed) return;
            var at = e.GetPosition(_list);
            if (!_drawing)
            {
                if (Math.Abs(at.X - _start.X) < ListDragReorder.StartDistance && Math.Abs(at.Y - _start.Y) < ListDragReorder.StartDistance) return;
                _drawing = true;
                _band.IsVisible = true;
            }
            var rect = new Rect(Math.Min(_start.X, at.X), Math.Min(_start.Y, at.Y), Math.Abs(at.X - _start.X), Math.Abs(at.Y - _start.Y))
                .Intersect(new Rect(_list.Bounds.Size));
            _band.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            _band.Width = rect.Width;
            _band.Height = rect.Height;
            Select(rect);
            e.Handled = true;
        }

        private void Select(Rect rect)
        {
            if (_list.SelectedItems is not { } selected) return;
            var hit = new List<object>(_before);
            for (var i = 0; i < _list.ItemCount; i++)
            {
                if (_list.ContainerFromIndex(i) is not Control row || row.TranslatePoint(new Point(0, 0), _list) is not { } origin) continue;
                if (new Rect(origin, row.Bounds.Size).Intersects(rect) && _list.Items.Cast<object>().ElementAtOrDefault(i) is { } item && !hit.Contains(item))
                    hit.Add(item);
            }
            if (selected.Count == hit.Count && hit.All(selected.Contains)) return; // nothing changed: leave the list alone
            selected.Clear();
            foreach (var item in hit) selected.Add(item);
        }

        private void OnUp(object? sender, PointerReleasedEventArgs e)
        {
            if (!_pressed) return;
            Stop();
            e.Pointer.Capture(null);
        }

        private void Stop()
        {
            _pressed = _drawing = false;
            _pointer = null;
            _band.IsVisible = false;
        }
    }
}
