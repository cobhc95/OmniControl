using Avalonia.Input;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// One QR code found in the selection: its text, and whether it is a web address the pop-down may open.
public sealed record QrItem(string Shown, bool IsLink, IRelayCommand Copy, IRelayCommand Open);

/// The area overlay of one monitor: its frozen picture, the selection (in the picture's pixels), the drawing tool and colour, the drawings
/// with undo and redo, and what the bars do. Pointer and key input arrive here from the view, already in picture pixels; this decides what
/// they mean. When the user is done the picture is exported and handed to <see cref="IShotOutput"/>.
public sealed partial class CaptureOverlayViewModel : ObservableObject, IDisposable
{
    private enum Drag { None, Select, Move, Resize, Draw }

    private readonly IShotOutput _output;
    private readonly Action _close;
    private readonly ScreenRect _monitor;
    private readonly bool _recordAtOnce;
    private readonly ToolSizes _sizes;
    private readonly List<Mark> _marks = new();
    private readonly UndoHistory _history = new();
    private Drag _drag;
    private bool _moved;

    /// A press that moves less than this (screen units) before it ends is a click.
    private const double ClickSlop = 3;
    private Grip _grip;
    private Point _dragStart;
    private ScreenRect _selectionAtStart;
    private Color _color = Palette.Drawing[0];

    private readonly ScreenRect _pictureBounds;

    /// The frozen picture (immutable, so reading a part back never copies all of it); released with the overlay.
    public Bitmap Picture { get; }

    /// The picture's width in pixels: the view's scale between screen units and picture pixels.
    public int PictureWidth => _pictureBounds.Width;

    /// The monitor's scale (1 = 96 dpi), set by the view: drawings are as thick on every monitor.
    public double Scale { get; set; } = 1;

    public IReadOnlyList<Mark> Marks => _marks;

    /// The drawing being made, shown until the press ends.
    public Mark? Preview { get; private set; }

    [ObservableProperty] private ScreenRect? _selection;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowBars))] private bool _isDragging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPen), nameof(IsLine), nameof(IsArrow), nameof(IsBox), nameof(IsMarker), nameof(IsText), nameof(IsBlur), nameof(HasSize))]
    private DrawTool _tool;

    [ObservableProperty] private IBrush _currentBrush;

    partial void OnToolChanged(DrawTool value) => TextCommitRequested?.Invoke();

    /// What the QR pop-down says instead of a list (still reading, nothing found).
    [ObservableProperty] private string _qrNote = string.Empty;

    public bool HasSelection => Selection is { IsEmpty: false };
    public bool ShowBars => HasSelection && !IsDragging;
    public string SizeText => Selection is { } s ? $"{s.Width} × {s.Height}" : Hovered is { } w ? HoverText(w) : string.Empty;

    /// Tells the bindings only what really changed: moving the selection changes neither whether there is one nor its size, so a move
    /// costs the bars and the size label nothing (the bindings are resolved by reflection, on every pointer move before).
    partial void OnSelectionChanged(ScreenRect? oldValue, ScreenRect? newValue)
    {
        if ((oldValue is { IsEmpty: false }) != (newValue is { IsEmpty: false }))
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowBars));
            OnPropertyChanged(nameof(ShowSize));
        }
        if (oldValue?.Width != newValue?.Width || oldValue?.Height != newValue?.Height) OnPropertyChanged(nameof(SizeText));
    }

    public bool IsPen => Tool == DrawTool.Pen;
    public bool IsLine => Tool == DrawTool.Line;
    public bool IsArrow => Tool == DrawTool.Arrow;
    public bool IsBox => Tool == DrawTool.Box;
    public bool IsMarker => Tool == DrawTool.Marker;
    public bool IsText => Tool == DrawTool.Text;
    public bool IsBlur => Tool == DrawTool.Blur;

    /// The tool in use has a size (line width or text size): the tool bar shows A- / A+.
    public bool HasSize => ToolSizes.Has(Tool);

    /// The text tool's size now (screen units): the open text box shows its words at this size.
    public double TextSize => _sizes.Of(DrawTool.Text);

    public IReadOnlyList<SwatchItem> Colours { get; }
    public ObservableCollection<QrItem> QrCodes { get; } = new();

    /// The selection changed (or the drawing in progress began or ended): the view moves its frame, clip and bars.
    public event Action? Redraw;

    /// A finished drawing came or went (drawn, undone, redone): the view repaints where it came or went.
    public event Action? MarksChanged;

    /// Only the drawing in progress changed (each pointer move while drawing): the view repaints only where it changed.
    public event Action? PreviewChanged;

    /// The text tool was pressed at this point (picture pixels): the view opens a box there.
    public event Action<Point>? TextRequested;

    /// Something other than the text box was used (another tool, a press on the picture, undo, copy, save, print, search, record): the view keeps
    /// the words typed so far as a drawing and removes the box. The box is a pop-up window, so it never loses focus to those by itself.
    public event Action? TextCommitRequested;

    /// This overlay began a new selection; the other monitors' overlays drop theirs.
    public event Action<CaptureOverlayViewModel>? SelectionStarted;

    /// <param name="picture">The frozen picture; the overlay owns it from now on and releases it in <see cref="Dispose"/>.</param>
    /// <param name="monitor">Where the picture sits on the desktop (physical pixels).</param>
    /// <param name="recordAtOnce">"Record area": recording starts as soon as a selection is drawn.</param>
    /// <param name="snap">This monitor's window edges and windows, in picture pixels.</param>
    /// <param name="choice">The magnet: snapping and window picking on or off.</param>
    public CaptureOverlayViewModel(Bitmap picture, ScreenRect monitor, IShotOutput output, Action close, bool recordAtOnce, ToolSizes sizes,
        WindowSnap snap, SnapChoice choice)
    {
        _snap = snap;
        _choice = choice;
        Picture = picture;
        _pictureBounds = new ScreenRect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height);
        _monitor = monitor;
        _output = output;
        _close = close;
        _recordAtOnce = recordAtOnce;
        _sizes = sizes;
        _currentBrush = new ImmutableSolidColorBrush(_color);
        Colours = Palette.Drawing.Select((c, i) => new SwatchItem(new ImmutableSolidColorBrush(c), new RelayCommand(() => PickColour(c)), Palette.DrawingTitles[i])
            { IsSelected = c == _color }).ToList();
    }

    /// Another monitor's overlay took the selection.
    public void DropSelection()
    {
        Selection = null;
        Redraw?.Invoke();
    }

    // ---- Pointer ----

    /// What a press at the point would grab, for the pointer's shape.
    public Grip GripAt(Point p, double reach) => Selection is { } sel ? SelectionMath.HitTest(sel, (int)p.X, (int)p.Y, (int)reach) : Grip.None;

    public void Press(Point p, double reach)
    {
        TextCommitRequested?.Invoke();
        var grip = GripAt(p, reach);
        if (Tool == DrawTool.Text && grip != Grip.None)
        {
            TextRequested?.Invoke(p);
            return;
        }
        _dragStart = p;
        if (grip is Grip.None)
        {
            if (Tool != DrawTool.None && HasSelection) return; // a drawing tool never throws the selection away
            _drag = Drag.Select;
            _pressedOn = Hovered; // a click without a drag picks this window
            SetHover(null);
            Selection = new ScreenRect((int)p.X, (int)p.Y, 0, 0);
            SelectionStarted?.Invoke(this);
        }
        else if (grip is Grip.Move)
        {
            _drag = Tool == DrawTool.None ? Drag.Move : Drag.Draw;
            if (_drag == Drag.Draw) Preview = NewMark(p);
        }
        else
        {
            _drag = Drag.Resize;
            _grip = grip;
        }
        _selectionAtStart = Selection ?? default;
        _moved = false;
        IsDragging = true;
        Redraw?.Invoke();
    }

    public void MoveTo(Point p)
    {
        var dx = (int)Math.Round(p.X - _dragStart.X);
        var dy = (int)Math.Round(p.Y - _dragStart.Y);
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) >= ClickSlop * Scale) _moved = true;
        switch (_drag)
        {
            case Drag.Select or Drag.Move or Drag.Resize: Selection = DragSelection(p, dx, dy); break;
            case Drag.Draw: Extend(p); PreviewChanged?.Invoke(); return;
            default: return;
        }
        Redraw?.Invoke();
    }

    public void Release(Point p)
    {
        if (_drag == Drag.None) return;
        MoveTo(p);
        if (_drag == Drag.Draw && Preview is { } preview) Commit(preview);
        var picked = _drag == Drag.Select && !_moved ? _pressedOn : null;
        if (!HasSelection) Selection = null;
        var drawn = _drag == Drag.Select && HasSelection && picked is null;
        Preview = null;
        _pressedOn = null;
        _guides.Clear();
        _drag = Drag.None;
        IsDragging = false;
        if (picked is not null)
        {
            Pick(picked);
            return;
        }
        Redraw?.Invoke();
        if (drawn && _recordAtOnce) Record();
    }

    private Mark? NewMark(Point p) => Tool switch
    {
        DrawTool.Pen => new StrokeMark(p, _color, _sizes.Of(Tool) * Scale, 1),
        DrawTool.Marker => new StrokeMark(p, _color, _sizes.Of(Tool) * Scale, MarkLook.MarkerOpacity),
        DrawTool.Line => new LineMark(p, _color, _sizes.Of(Tool) * Scale, arrow: false),
        DrawTool.Arrow => new LineMark(p, _color, _sizes.Of(Tool) * Scale, arrow: true),
        DrawTool.Box => new BoxMark(p, _color, _sizes.Of(Tool) * Scale),
        DrawTool.Blur => new BoxMark(p, _color, MarkLook.Stroke * Scale), // the blur shows its frame while it is dragged
        _ => null,
    };

    private void Extend(Point p)
    {
        switch (Preview)
        {
            case StrokeMark stroke: stroke.Extend(p); break;
            case LineMark line: line.To = p; break;
            case BoxMark box: box.To = p; break;
        }
    }

    private void Commit(Mark preview)
    {
        if (Tool == DrawTool.Blur && preview is BoxMark box)
        {
            var area = SelectionMath.FromDrag((int)box.From.X, (int)box.From.Y, (int)box.To.X, (int)box.To.Y, _pictureBounds);
            if (area.IsEmpty) return;
            AddMark(new BlurMark(ImageBitmaps.Crop(Picture, area), area, Math.Max(1, (int)Math.Round(MarkLook.BlurBlock * Scale))));
        }
        else AddMark(preview);
        Changed();
    }

    /// The text box was closed with words in it: they become a drawing at the point it was opened.
    public void AddText(Point at, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AddMark(new TextMark(at, text, _color, TextSize * Scale));
        Changed();
    }

    // ---- Keys ----

    /// The overlay's keys: Esc closes, M switches the magnet, Tab / Shift+Tab and Enter light and pick a window before a selection exists, Ctrl+C/S/P copy, save and print, Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes, Ctrl+Shift+. and
    /// Ctrl+Shift+, make the tool's size bigger and smaller, the arrows nudge. True when the key was used.
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (HandleSizeKey(key, modifiers)) return true;
        if (_history.HandleKey(key, modifiers))
        {
            Changed();
            return true;
        }
        switch (key)
        {
            case Key.Escape: Close(); return true;
            case Key.M when modifiers == KeyModifiers.None: ToggleSnap(); return true;
            case Key.Tab when !ctrl: return StepWindow(shift ? -1 : 1);
            case Key.Enter when modifiers == KeyModifiers.None: return PickHovered();
            case Key.C when ctrl: Copy(); return true;
            case Key.S when ctrl: Save(); return true;
            case Key.P when ctrl: Print(); return true;
            case Key.Left: return Nudge(-1, 0, shift);
            case Key.Right: return Nudge(1, 0, shift);
            case Key.Up: return Nudge(0, -1, shift);
            case Key.Down: return Nudge(0, 1, shift);
            default: return false;
        }
    }

    /// Ctrl+Shift+. (>) one size bigger, Ctrl+Shift+, (<) one smaller, for the tool in use; also while its text box has the keys.
    public bool HandleSizeKey(Key key, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control) || !modifiers.HasFlag(KeyModifiers.Shift)) return false;
        if (key == Key.OemPeriod) StepSize(1);
        else if (key == Key.OemComma) StepSize(-1);
        else return false;
        return true;
    }

    /// The tool's size one step bigger (+1) or smaller (-1): A+ / A-, the keys, the wheel over the colour.
    public void StepSize(int direction)
    {
        if (_sizes.Step(Tool, direction) && Tool == DrawTool.Text) OnPropertyChanged(nameof(TextSize));
    }

    [RelayCommand] private void Bigger() => StepSize(1);
    [RelayCommand] private void Smaller() => StepSize(-1);

    private bool Nudge(int dx, int dy, bool large)
    {
        if (Selection is not { IsEmpty: false } sel) return false;
        Selection = SelectionMath.Nudge(sel, dx, dy, large, _pictureBounds);
        Redraw?.Invoke();
        return true;
    }

    // ---- Bars ----

    [RelayCommand]
    private void PickTool(DrawTool tool) => Tool = Tool == tool ? DrawTool.None : tool; // a second click puts the tool down

    private void PickColour(Color color)
    {
        _color = color;
        CurrentBrush = new ImmutableSolidColorBrush(color);
        foreach (var dot in Colours) dot.IsSelected = dot.Brush is ISolidColorBrush { Color: var c } && c == color;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        TextCommitRequested?.Invoke();
        if (!_history.CanUndo) return;
        _history.Undo();
        Changed();
    }

    private bool CanUndo() => _history.CanUndo;

    /// Each finished drawing is one step; one undone and then replaced by a new drawing is released.
    private void AddMark(Mark mark)
    {
        _marks.Add(mark);
        _history.Record("drawing", () => _marks.Remove(mark), () => _marks.Add(mark), forget: mark.Dispose);
    }

    private void Changed()
    {
        NotifyUndo();
        MarksChanged?.Invoke();
    }

    private void NotifyUndo() => UndoCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void Copy() => Finish(_output.Copy);
    [RelayCommand] private void Save() => Finish(_output.Save);
    [RelayCommand] private void Print() => Finish(_output.Print);
    [RelayCommand] private void Search() => Finish(_output.SearchOnline);
    [RelayCommand] private void Close() => _close();

    /// Closes the overlay and records the selection as a video (drawings are not recorded).
    [RelayCommand]
    private void Record()
    {
        if (Selection is not { IsEmpty: false } sel) return;
        var area = sel.Offset(_monitor.X, _monitor.Y);
        // Posted: this runs inside the picture window's own message, which the close destroys, and the recording's windows belong to the UI thread's own turn
        Dispatcher.UIThread.Post(() =>
        {
            _close();
            _output.Record(area, _monitor, Scale);
        });
    }

    /// Reads the QR codes in the selection (drawings left out) for the pop-down: one row per code, or one plain line.
    [RelayCommand]
    private async Task ReadQr()
    {
        if (Selection is not { IsEmpty: false } sel) return;
        QrCodes.Clear();
        QrNote = "Looking for codes…";
        IReadOnlyList<ScannedCode> found;
        try
        {
            found = await _output.ReadQrAsync(ImageBitmaps.Crop(Picture, sel));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            found = Array.Empty<ScannedCode>(); // a picture the reader cannot use holds no code it can read
        }
        foreach (var (kind, text) in found)
        {
            var link = ScreenshotService.IsWebLink(text);
            QrCodes.Add(new QrItem($"{kind}: {text}", link, new RelayCommand(() => _output.CopyText(text)), new RelayCommand(() => _output.OpenLink(text))));
        }
        QrNote = found.Count == 0 ? "No code found." : string.Empty;
    }

    /// Exports the selection with its drawings, closes the overlay, then hands the picture on (so a print dialog is not under the overlay).
    private void Finish(Action<ScreenImage> use)
    {
        TextCommitRequested?.Invoke();
        if (Selection is not { IsEmpty: false } sel) return;
        var image = ImageBitmaps.Render(Picture, sel, Marks);
        _close();
        use(image);
    }

    public void Dispose()
    {
        _history.Clear();
        foreach (var mark in _marks) mark.Dispose();
        _marks.Clear();
        Preview?.Dispose();
        Picture.Dispose();
    }
}
