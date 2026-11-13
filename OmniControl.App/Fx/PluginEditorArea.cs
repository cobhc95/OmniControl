using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// The space a plug-in's own editor (VST3 or VST2) shows in: the FX window's editor pane and a popped-out editor window (adapted from
/// TabForge's <c>FxEditorArea</c>, same owner). When an editor arrives or asks for a new size, the window grows to show it whole, never past the screen's working area (<see cref="EditorSizing"/>,
/// <see cref="EditorFit"/>; the display scale is taken into account). What still does not fit (an editor wider than the screen) is
/// scrolled with the two bars, which move the plug-in's own window inside its host. A plug-in that can resize is offered the space
/// when it changes. If the editor can't be attached, the area says why in one sentence.
/// <see cref="UserScale"/> (the effect's Scale) is asked of the plug-in on top of the display scale, once the editor is shown and
/// whenever either changes; the space then follows the editor's new size. A plug-in that ignores it raises <see cref="ScaleRefused"/>.
/// The editor's owner opens and disposes it; the area only shows it.
/// </summary>
internal sealed class PluginEditorArea : Grid
{
    public static readonly StyledProperty<IPluginEditor?> EditorProperty = AvaloniaProperty.Register<PluginEditorArea, IPluginEditor?>(nameof(Editor));

    /// <summary>How the space follows the editor's size (see <see cref="EditorSizing"/>).</summary>
    public static readonly StyledProperty<EditorSizing> SizingProperty = AvaloniaProperty.Register<PluginEditorArea, EditorSizing>(nameof(Sizing));

    /// <summary>The user's editor scale (1 = the editor's own size), asked of the plug-in on top of the display scale.</summary>
    public static readonly StyledProperty<double> UserScaleProperty = AvaloniaProperty.Register<PluginEditorArea, double>(nameof(UserScale), 1);

    private readonly Border _frame = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _message = new() { Classes = { "hint" }, TextWrapping = TextWrapping.Wrap, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollBar _horizontal = Bar(Orientation.Horizontal);
    private readonly ScrollBar _vertical = Bar(Orientation.Vertical);
    private (int Width, int Height) _pixels;
    private TopLevel? _top;

    /// <summary>Runs the shown editor's idle call (<see cref="IPluginEditor.Idle"/>) while it is in the frame.</summary>
    private readonly DispatcherTimer _idle = new() { Interval = Services.Timing.PluginEditorIdle };

    public PluginEditorArea()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent; // the wheel reaches the space beside the editor and the bars
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("*,Auto");
        Children.Add(_message);
        Children.Add(_frame);
        SetColumn(_vertical, 1);
        SetRow(_horizontal, 1);
        Children.Add(_vertical);
        Children.Add(_horizontal);
        _horizontal.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) MoveView(); };
        _vertical.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) MoveView(); };
        SizeChanged += (_, _) => OnSpaceChanged();
        PointerWheelChanged += OnWheel;
        _idle.Tick += (_, _) => Editor?.Idle();
    }

    public IPluginEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public EditorSizing Sizing
    {
        get => GetValue(SizingProperty);
        set => SetValue(SizingProperty, value);
    }

    public double UserScale
    {
        get => GetValue(UserScaleProperty);
        set => SetValue(UserScaleProperty, value);
    }

    /// <summary>The plug-in did not change its editor's size when asked for <see cref="UserScale"/>: it can't be scaled by OmniControl.</summary>
    public event Action? ScaleRefused;

    /// <summary>A bar in the app's master scroll bar style (Styles/ScrollBars.axaml), which also gives its thickness.</summary>
    private static ScrollBar Bar(Orientation orientation) => new()
    {
        Orientation = orientation, AllowAutoHide = false, IsVisible = false, SmallChange = EditorFit.ScrollStep,
    };

    /// <summary>The thickness the master style gives a scroll bar (its least width), which a shown bar takes from the editor's space.</summary>
    private double BarThickness => _vertical.MinWidth;

    private double Scale => _top?.RenderScaling ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UserScaleProperty) { ApplyScale(reportRefusal: true); return; }
        if (change.Property != EditorProperty) return;
        if (change.OldValue is IPluginEditor old) old.Resized -= OnEditorResized;
        _idle.Stop();
        _frame.Child = null; // the host's native window goes on Avalonia's next pass and detaches the old editor then
        _message.IsVisible = false;
        _horizontal.Value = _vertical.Value = 0;
        if (change.NewValue is not IPluginEditor editor) { _pixels = (0, 0); Fit(); return; }
        editor.Resized += OnEditorResized;
        _pixels = editor.Size;
        Dispatcher.UIThread.Post(() => Dock(editor), DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top is not null) _top.ScalingChanged += OnScalingChanged;
        if (Editor is not { } editor) return;
        _idle.Start(); // the editor's host comes back with the area; an editor not yet attached ignores the call
        Dispatcher.UIThread.Post(() => Dock(editor), DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null) _top.ScalingChanged -= OnScalingChanged;
        _top = null;
        _idle.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Puts the editor's host in the frame once the window is shown (the native child window is then made in an ordinary layout pass,
    /// as TabForge found), then grows the window for it. A refusal by Windows or the plug-in is shown as one sentence.
    /// </summary>
    private void Dock(IPluginEditor editor)
    {
        if (!ReferenceEquals(Editor, editor) || _frame.Child is not null || TopLevel.GetTopLevel(this) is null) return;
        var host = new PluginEditorHost(editor);
        // The failure is reported while the native window is being made: change the area only after that call has returned.
        host.Failed += reason => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(Editor, editor)) ShowMessage(reason); });
        try
        {
            _frame.Child = host;
            UpdateLayout();
        }
        catch (InvalidOperationException)
        {
            // NativeControlHost throws this when Windows refuses the child window: the window stays open without the editor.
            _frame.Child = null;
            ShowMessage("Windows did not allow the plug-in's window here.");
            return;
        }
        _idle.Start();
        ApplyScale(reportRefusal: true);
        Grow();
        OnSpaceChanged();
    }

    /// <summary>
    /// Asks the shown editor for the display scale times <see cref="UserScale"/>; when it takes it, the window and the bars follow its
    /// new size. A refusal is reported only for the user's own choice (the display scale alone is not the user's doing).
    /// </summary>
    private void ApplyScale(bool reportRefusal)
    {
        if (Editor is not { IsOpen: true } editor || _frame.Child is null) return; // a closed editor is on its way out of the area
        if (!editor.TryScale(Scale * UserScale))
        {
            if (reportRefusal && UserScale != 1) ScaleRefused?.Invoke();
            return;
        }
        _pixels = editor.Size;
        Grow();
        Fit();
    }

    private void ShowMessage(string reason)
    {
        _idle.Stop();
        _frame.Child = null;
        _message.Text = $"The plug-in's window can't be shown here: {reason}";
        _message.IsVisible = true;
        _pixels = (0, 0);
        Fit();
    }

    /// <summary>The plug-in asked for a new size (IPlugFrame::resizeView): the window adapts, then the bars.</summary>
    private void OnEditorResized(int width, int height)
    {
        _pixels = (width, height);
        Grow();
        Fit();
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        ApplyScale(reportRefusal: false);
        if (Editor is { } editor) _pixels = editor.Size;
        Fit();
    }

    /// <summary>The user resized the window: a plug-in that can resize is offered the whole space; any editor gets its bars again.</summary>
    private void OnSpaceChanged()
    {
        if (Editor is { CanResize: true } editor && _frame.Child is not null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var space = EditorFit.ToPixels(Bounds.Width, Bounds.Height, Scale);
            if (space != _pixels) _pixels = editor.Offer(space.Width, space.Height);
        }
        Fit();
    }

    /// <summary>
    /// Makes room for the whole editor within the working area of its screen: the window grows (TabForge's GrowWindow).
    /// </summary>
    private void Grow()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || _pixels.Width <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0
            || window.WindowState != WindowState.Normal || window.Screens.ScreenFromWindow(window) is not { } screen) return;
        var scale = window.RenderScaling; // window position and working area are pixels, window size is DIPs
        var area = screen.WorkingArea;
        var work = new EditorFit.Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
        var editor = EditorFit.ToDip(_pixels.Width, _pixels.Height, scale);
        var now = new EditorFit.Rect(window.Position.X / scale, window.Position.Y / scale, window.Bounds.Width, window.Bounds.Height);
        var chrome = (Math.Max(0, now.Width - Bounds.Width), Math.Max(0, now.Height - Bounds.Height));
        var fit = EditorFit.FitWindow(now, chrome, editor, work, growOnly: Sizing == EditorSizing.GrowWindow);
        if (fit == now) return;
        window.Position = new PixelPoint((int)Math.Round(fit.Left * scale), (int)Math.Round(fit.Top * scale));
        window.Width = fit.Width;
        window.Height = fit.Height;
    }

    /// <summary>Sizes the host frame to the part of the editor that fits and shows the bars the rest needs.</summary>
    private void Fit()
    {
        if (_frame.Child is null || _pixels.Width <= 0)
        {
            _horizontal.IsVisible = _vertical.IsVisible = false;
            _horizontal.Maximum = _vertical.Maximum = 0;
            return;
        }
        var (w, h) = EditorFit.ToDip(_pixels.Width, _pixels.Height, Scale);
        var layout = EditorFit.Scroll(Bounds.Width, Bounds.Height, w, h, BarThickness);
        _frame.Width = layout.ViewWidth;
        _frame.Height = layout.ViewHeight;
        SetBar(_horizontal, layout.RangeX, layout.ViewWidth);
        SetBar(_vertical, layout.RangeY, layout.ViewHeight);
        MoveView();
    }

    private static void SetBar(ScrollBar bar, double range, double view)
    {
        bar.IsVisible = range > 0;
        bar.ViewportSize = view;
        bar.LargeChange = Math.Max(EditorFit.ScrollStep, view - EditorFit.ScrollStep);
        bar.Maximum = range; // a smaller range pulls the value in, which moves the view
    }

    private void MoveView()
    {
        var (x, y) = EditorFit.ViewOrigin(_horizontal.Value, _vertical.Value, Scale);
        Editor?.MoveView(x, y);
    }

    /// <summary>The wheel over the bars or beside the editor scrolls it (over the editor itself the wheel belongs to the plug-in).</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var bar = _vertical.IsVisible && (e.KeyModifiers & KeyModifiers.Shift) == 0 ? _vertical : _horizontal.IsVisible ? _horizontal : null;
        if (bar is null) return;
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        bar.Value = Math.Clamp(bar.Value - delta * EditorFit.ScrollStep, 0, bar.Maximum);
        e.Handled = true;
    }
}
