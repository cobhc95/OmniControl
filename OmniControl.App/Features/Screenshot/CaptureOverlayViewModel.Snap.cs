using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// Snapping to windows and picking a window (docs/mockups/screenshot-snap.html): while the magnet is on, a dragged edge jumps onto a
/// window, taskbar or monitor edge (<see cref="WindowSnap"/>) and the snapped edges show as guides; before a selection exists the window
/// under the pointer is lit and a click (or Enter, after Tab / Shift+Tab stepped to it) selects it. Alt held turns snapping off for that drag.
public sealed partial class CaptureOverlayViewModel
{
    private const int NameLimit = 60;

    private readonly WindowSnap _snap;
    private readonly SnapChoice _choice;
    private readonly List<SnapGuide> _guides = new();
    private DesktopWindow? _pressedOn;
    private int _keyWindow = -1;

    /// The window lit under the pointer (or stepped to with Tab) while there is no selection; null = none.
    public DesktopWindow? Hovered { get; private set; }

    /// The edges the selection is snapped to now (only while a drag goes on).
    public IReadOnlyList<SnapGuide> Guides => _guides;

    /// The pointer is over this overlay's monitor with a window lit: the other monitors' overlays drop theirs.
    public event Action<CaptureOverlayViewModel>? HoverStarted;

    /// An Alt key is down: snapping pauses and the magnet dims.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MagnetLit), nameof(MagnetPaused))] private bool _altHeld;

    public bool SnapOn => _choice.On;
    public bool MagnetLit => SnapOn && !AltHeld;
    public bool MagnetPaused => SnapOn && AltHeld;

    public string MagnetTip => $"Snap to windows: {(SnapOn ? "on" : "off")}\nEdges snap to window borders, and a click selects a whole window.\nHold Alt to ignore it for one drag · M";

    /// The size label shows the selection's size, or the lit window's name and size.
    public bool ShowSize => HasSelection || Hovered is not null;

    private bool Snapping => SnapOn && !AltHeld;

    /// How far an edge jumps, in picture pixels on this monitor.
    private int Reach => (int)Math.Round(WindowSnap.Distance * Scale);

    private string HoverText(DesktopWindow window)
    {
        var name = window.Name.Length > NameLimit ? window.Name[..(NameLimit - 1)] + "…" : window.Name;
        return $"{name} · {window.Frame.Width} × {window.Frame.Height}";
    }

    /// The pointer moved without a button: the window under it is lit while there is no selection and the magnet is on.
    public void HoverAt(Point p)
    {
        _keyWindow = -1;
        SetHover(HasSelection || IsDragging || !SnapOn ? null : _snap.WindowAt((int)p.X, (int)p.Y));
    }

    /// Another monitor's overlay lit a window.
    public void DropHover() => SetHover(null);

    private void SetHover(DesktopWindow? window)
    {
        if (window == Hovered) return;
        Hovered = window;
        OnPropertyChanged(nameof(ShowSize));
        OnPropertyChanged(nameof(SizeText));
        if (window is not null) HoverStarted?.Invoke(this);
        Redraw?.Invoke();
    }

    /// Tab (+1) or Shift+Tab (-1) without a selection: lights the next window on this monitor, front to back.
    private bool StepWindow(int direction)
    {
        var windows = _snap.Windows;
        if (HasSelection || !SnapOn || windows.Count == 0) return false;
        _keyWindow = ((_keyWindow < 0 && direction < 0 ? 0 : _keyWindow + direction) % windows.Count + windows.Count) % windows.Count;
        SetHover(windows[_keyWindow]);
        return true;
    }

    /// Enter without a selection: the lit window becomes the selection.
    private bool PickHovered()
    {
        if (HasSelection || Hovered is not { } window) return false;
        Pick(window);
        return true;
    }

    private void Pick(DesktopWindow window)
    {
        SelectionStarted?.Invoke(this);
        SetHover(null);
        Selection = window.Frame.Intersect(_pictureBounds);
        Redraw?.Invoke();
        if (_recordAtOnce && HasSelection) Record();
    }

    /// The magnet button and M: snapping and window picking on or off, kept for the next screenshot.
    [RelayCommand]
    private void ToggleSnap()
    {
        _choice.Toggle();
        OnPropertyChanged(nameof(SnapOn));
        OnPropertyChanged(nameof(MagnetLit));
        OnPropertyChanged(nameof(MagnetPaused));
        OnPropertyChanged(nameof(MagnetTip));
        if (!SnapOn) SetHover(null);
    }

    /// The selection a drag makes now, snapped unless the magnet is off or Alt is held.
    private ScreenRect DragSelection(Point p, int dx, int dy)
    {
        _guides.Clear();
        var snap = Snapping;
        return _drag switch
        {
            Drag.Select when snap => _snap.Drag((int)_dragStart.X, (int)_dragStart.Y, (int)p.X, (int)p.Y, _pictureBounds, Reach, _guides),
            Drag.Select => SelectionMath.FromDrag((int)_dragStart.X, (int)_dragStart.Y, (int)p.X, (int)p.Y, _pictureBounds),
            Drag.Move when snap => _snap.Move(_selectionAtStart, dx, dy, _pictureBounds, Reach, _guides),
            Drag.Move => SelectionMath.Move(_selectionAtStart, dx, dy, _pictureBounds),
            Drag.Resize when snap => _snap.Resize(_selectionAtStart, _grip, dx, dy, _pictureBounds, Reach, _guides),
            _ => SelectionMath.Resize(_selectionAtStart, _grip, dx, dy, _pictureBounds),
        };
    }
}
