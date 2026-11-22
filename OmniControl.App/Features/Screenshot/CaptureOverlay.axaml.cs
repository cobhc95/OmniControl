using Avalonia.Controls.Primitives;
using Avalonia.Input;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// One monitor's overlay window: fills its monitor (full screen at the monitor's own scale), shows the picture (<see cref="CaptureCanvas"/>),
/// places the bars beside the selection and hands keys and the text box to the view-model. Layout and wiring only.
public partial class CaptureOverlay : Window
{
    private readonly CaptureOverlayViewModel _model;
    private readonly bool _strong;
    private readonly double _gap;
    private readonly OncePerFrame _placeBars;
    private readonly CaptureCanvas? _picture;
    private Point _textAt;

    /// The monitor this overlay covers, in physical pixels.
    public ScreenRect Monitor { get; }

    public CaptureOverlay() : this(null!, default, false) { } // the designer only

    public CaptureOverlay(CaptureOverlayViewModel model, ScreenRect monitor, bool strong)
    {
        InitializeComponent();
        _model = model;
        _strong = strong;
        Monitor = monitor;
        DataContext = model;
        _placeBars = new OncePerFrame(this, "Screenshot overlay: placing the bars", PlaceBars);
        _gap = Application.Current?.TryFindResource("CaptureBarGap", out var gap) == true && gap is double g ? g : 0;
        Position = new PixelPoint(monitor.X, monitor.Y);
        Width = monitor.Width;
        Height = monitor.Height;
        if (model is null) return;
        Root.Children.Insert(0, _picture = new CaptureCanvas(model)); // under the pop-ups; made here because it needs the view-model
        model.Redraw += _placeBars.Request;
        model.TextRequested += OpenTextBox;
        model.TextCommitRequested += CommitText;
        Opened += OnOpened;
        Closed += OnClosed;
        SizeLabel.SizeChanged += OnBarSizeChanged;
        ToolBar.SizeChanged += OnBarSizeChanged;
        ActionBar.SizeChanged += OnBarSizeChanged;
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
        _model.Redraw -= _placeBars.Request;
        _model.TextRequested -= OpenTextBox;
        _model.TextCommitRequested -= CommitText;
        Opened -= OnOpened;
        Closed -= OnClosed;
        SizeLabel.SizeChanged -= OnBarSizeChanged;
        ToolBar.SizeChanged -= OnBarSizeChanged;
        ActionBar.SizeChanged -= OnBarSizeChanged;
        TextEntry.KeyDown -= OnTextKey;
        TextEntry.LostFocus -= OnTextLostFocus;
        ColourButton.PointerWheelChanged -= OnColourWheel;
        _picture?.Dispose();
    }

    /// The wheel over the colour button steps the tool's size (mouse first).
    private void OnColourWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        _model.StepSize(ValueWheel.Direction(e));
        e.Handled = true;
    }

    private IntPtr? Handle => TryGetPlatformHandle()?.Handle;

    /// Activates the overlay and makes it the foreground window, even over another program.
    public void TakeFocus()
    {
        Activate();
        if (Handle is { } handle) OverlayTopmost.TakeForeground(handle);
        Focus();
    }

    /// The strong mode's refresh: back to the top of the top-most windows.
    public void PutOnTop()
    {
        if (Handle is { } handle) OverlayTopmost.PutOnTop(handle);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!TextPopup.IsOpen && _model.HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
        base.OnKeyDown(e);
    }

    /// A bar took its size (it opened, or its buttons changed): placed again at once, so it never shows at a stale place.
    private void OnBarSizeChanged(object? sender, SizeChangedEventArgs e) => PlaceBars();

    /// Puts the size label above the selection's top-left corner, the toolbar beside it and the action bar under it, each kept on screen.
    private void PlaceBars()
    {
        if (_model.Selection is not { IsEmpty: false } sel || Bounds.Width <= 0) return;
        var k = Bounds.Width / _model.PictureWidth;
        double x = sel.X * k, y = sel.Y * k, w = sel.Width * k, h = sel.Height * k;
        var tool = SelectionMath.PlaceBar(BarSide.Right, x, y, w, h, ToolBar.Bounds.Width, ToolBar.Bounds.Height, Bounds.Width, Bounds.Height, _gap);
        Place(ToolPopup, tool);
        Place(ActionPopup, SelectionMath.PlaceBarAvoiding(x, y, w, h, ActionBar.Bounds.Width, ActionBar.Bounds.Height,
            (tool.X, tool.Y, ToolBar.Bounds.Width, ToolBar.Bounds.Height), Bounds.Width, Bounds.Height, _gap));
        var labelY = y - _gap - SizeLabel.Bounds.Height;
        Place(SizePopup, (x, labelY >= 0 ? labelY : y + _gap));
    }

    /// Moves a pop-up to a point of the window (screen units); Windows only moves its small window, nothing is drawn again.
    private static void Place(Popup popup, (double X, double Y) at)
    {
        if (popup.HorizontalOffset != at.X) popup.HorizontalOffset = at.X;
        if (popup.VerticalOffset != at.Y) popup.VerticalOffset = at.Y;
    }

    // ---- The text tool's box ----

    private void OpenTextBox(Point at)
    {
        CommitText();
        _textAt = at;
        var k = Bounds.Width / _model.PictureWidth;
        Place(TextPopup, (at.X * k, at.Y * k));
        TextEntry.Text = string.Empty;
        TextPopup.IsOpen = true;
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

    /// Clicking away keeps the words, except into the colour pop-down: the colour then applies to the words still being typed.
    private void OnTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ColourButton.Flyout?.IsOpen != true) CommitText();
    }

    private void CommitText()
    {
        if (!TextPopup.IsOpen) return;
        var text = TextEntry.Text ?? string.Empty;
        CloseTextBox();
        _model.AddText(_textAt, text);
    }

    private void CloseTextBox()
    {
        TextPopup.IsOpen = false;
        TextEntry.Text = string.Empty;
    }

    /// Closes the colour pop-down after a colour was clicked. A button raises Click before it runs its command, and a closed flyout loses
    /// its place in the window's tree, so closing it here at once cut the swatch off from its data and its command never ran: the colour
    /// did not change. Closing is therefore put after the click is fully handled.
    private void OnSwatchPicked(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        ColourButton.Flyout?.Hide();
        if (TextPopup.IsOpen) TextEntry.Focus(); // back to the words being typed, now in the new colour
    });
}
