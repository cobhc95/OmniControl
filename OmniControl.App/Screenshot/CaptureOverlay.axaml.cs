using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// One monitor's overlay window: fills its monitor (full screen at the monitor's own scale), shows the canvas, places the bars beside the
/// selection and hands keys and the text box to the view-model. Layout and wiring only.
/// </summary>
public partial class CaptureOverlay : Window
{
    private readonly CaptureOverlayViewModel _model;
    private readonly bool _strong;
    private readonly double _gap;
    private Point _textAt;

    /// <summary>The monitor this overlay covers, in physical pixels.</summary>
    public ScreenRect Monitor { get; }

    public CaptureOverlay() : this(null!, default, false) { } // the designer only

    public CaptureOverlay(CaptureOverlayViewModel model, ScreenRect monitor, bool strong)
    {
        InitializeComponent();
        _model = model;
        _strong = strong;
        Monitor = monitor;
        DataContext = model;
        Picture.Model = model;
        _gap = Application.Current?.TryFindResource("CaptureBarGap", out var gap) == true && gap is double g ? g : 0;
        Position = new PixelPoint(monitor.X, monitor.Y);
        Width = monitor.Width;
        Height = monitor.Height;
        if (model is null) return;
        model.Redraw += PlaceBars;
        model.TextRequested += OpenTextBox;
        Opened += OnOpened;
        Closed += OnClosed;
        Layer.LayoutUpdated += OnLayoutUpdated;
        TextEntry.KeyDown += OnTextKey;
        ColourButton.PointerWheelChanged += OnColourWheel;
        TextEntry.LostFocus += OnTextLostFocus;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        WindowState = WindowState.FullScreen; // full screen on the monitor the window was placed on, at that monitor's scale
        _model.Scale = RenderScaling;
        if (_strong && Handle is { } handle) OverlayTopmost.MakeStrong(handle);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _model.Redraw -= PlaceBars;
        _model.TextRequested -= OpenTextBox;
        Opened -= OnOpened;
        Closed -= OnClosed;
        Layer.LayoutUpdated -= OnLayoutUpdated;
        TextEntry.KeyDown -= OnTextKey;
        TextEntry.LostFocus -= OnTextLostFocus;
        ColourButton.PointerWheelChanged -= OnColourWheel;
    }

    /// <summary>The wheel over the colour button steps the tool's size (mouse first, AGENTS rule 5).</summary>
    private void OnColourWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        _model.StepSize(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    private IntPtr? Handle => TryGetPlatformHandle()?.Handle;

    /// <summary>Activates the overlay and makes it the foreground window, even over another program.</summary>
    public void TakeFocus()
    {
        Activate();
        if (Handle is { } handle) OverlayTopmost.TakeForeground(handle);
        Focus();
    }

    /// <summary>The strong mode's refresh: back to the top of the top-most windows.</summary>
    public void PutOnTop()
    {
        if (Handle is { } handle) OverlayTopmost.PutOnTop(handle);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!TextEntry.IsVisible && _model.HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
        base.OnKeyDown(e);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => PlaceBars();

    /// <summary>Puts the size label above the selection's top-left corner, the toolbar beside it and the action bar under it, each kept on screen.</summary>
    private void PlaceBars()
    {
        if (_model.Selection is not { IsEmpty: false } sel || Bounds.Width <= 0) return;
        var k = Bounds.Width / _model.PictureWidth;
        double x = sel.X * k, y = sel.Y * k, w = sel.Width * k, h = sel.Height * k;
        Place(ToolBar, SelectionMath.PlaceBar(BarSide.Right, x, y, w, h, ToolBar.Bounds.Width, ToolBar.Bounds.Height, Bounds.Width, Bounds.Height, _gap));
        Place(ActionBar, SelectionMath.PlaceBar(BarSide.Below, x, y, w, h, ActionBar.Bounds.Width, ActionBar.Bounds.Height, Bounds.Width, Bounds.Height, _gap));
        var labelY = y - _gap - SizeLabel.Bounds.Height;
        Place(SizeLabel, (x, labelY >= 0 ? labelY : y + _gap));
    }

    private static void Place(Control control, (double X, double Y) at)
    {
        if (Canvas.GetLeft(control) != at.X) Canvas.SetLeft(control, at.X);
        if (Canvas.GetTop(control) != at.Y) Canvas.SetTop(control, at.Y);
    }

    // ---- The text tool's box ----

    private void OpenTextBox(Point at)
    {
        CommitText();
        _textAt = at;
        var k = Bounds.Width / _model.PictureWidth;
        Canvas.SetLeft(TextEntry, at.X * k);
        Canvas.SetTop(TextEntry, at.Y * k);
        TextEntry.Text = string.Empty;
        TextEntry.IsVisible = true;
        TextEntry.Focus();
    }

    private void OnTextKey(object? sender, KeyEventArgs e)
    {
        if (_model.HandleSizeKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true; // the box follows the size at once and keeps the keys
            return;
        }
        if (e.Key == Key.Enter) CommitText();
        else if (e.Key == Key.Escape) CloseTextBox();
        else return;
        e.Handled = true;
        Focus();
    }

    /// <summary>Clicking away keeps the words, except into the colour pop-down: the colour then applies to the words still being typed.</summary>
    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ColourButton.Flyout?.IsOpen != true) CommitText();
    }

    private void CommitText()
    {
        if (!TextEntry.IsVisible) return;
        var text = TextEntry.Text ?? string.Empty;
        CloseTextBox();
        _model.AddText(_textAt, text);
    }

    private void CloseTextBox()
    {
        TextEntry.IsVisible = false;
        TextEntry.Text = string.Empty;
    }

    private void OnSwatchPicked(object? sender, RoutedEventArgs e)
    {
        ColourButton.Flyout?.Hide();
        if (TextEntry.IsVisible) TextEntry.Focus(); // back to the words being typed, now in the new colour
    }
}
