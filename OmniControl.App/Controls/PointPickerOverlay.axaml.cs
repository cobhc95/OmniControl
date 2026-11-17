using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Controls;

/// <summary>One monitor's cover of the point picker: dims it, follows the pointer with the x, y label and reports a click or Esc. Layout and wiring only.</summary>
public partial class PointPickerOverlay : Window
{
    private const double LabelOffset = 18;

    /// <summary>The monitor this window covers, in physical pixels.</summary>
    public ScreenRect Monitor { get; }

    /// <summary>The pointer moved over this monitor: the other monitors hide their label.</summary>
    public event Action<PointPickerOverlay>? Moved;

    /// <summary>The left button was released here (a point is picked), or null: Esc or the right button (cancelled).</summary>
    public event Action<PixelPoint?>? Finished;

    public PointPickerOverlay() : this(default) { } // the designer only

    public PointPickerOverlay(ScreenRect monitor)
    {
        InitializeComponent();
        Monitor = monitor;
        Position = new PixelPoint(monitor.X, monitor.Y);
        Width = monitor.Width;
        Height = monitor.Height;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        WindowState = WindowState.FullScreen; // full screen on the monitor the window was placed on
    }

    private IntPtr? Handle => TryGetPlatformHandle()?.Handle;

    /// <summary>Activates the overlay and makes it the foreground window, even over another program, so Esc reaches it.</summary>
    public void TakeFocus()
    {
        Activate();
        if (Handle is { } handle) OverlayTopmost.TakeForeground(handle);
        Focus();
    }

    /// <summary>Shows the x, y label beside the pointer (<paramref name="at"/> in this window's own coordinates), or hides it.</summary>
    public void ShowLabel(Point? at, string text)
    {
        Label.IsVisible = at is not null;
        if (at is not { } point) return;
        LabelText.Text = text;
        var x = point.X + LabelOffset;
        var y = point.Y + LabelOffset;
        if (x + Label.Bounds.Width > Bounds.Width) x = point.X - LabelOffset - Label.Bounds.Width;
        if (y + Label.Bounds.Height > Bounds.Height) y = point.Y - LabelOffset - Label.Bounds.Height;
        Canvas.SetLeft(Label, x);
        Canvas.SetTop(Label, y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        var screen = this.PointToScreen(at);
        ShowLabel(at, $"{screen.X}, {screen.Y}");
        Moved?.Invoke(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        switch (e.InitialPressMouseButton)
        {
            case MouseButton.Left: Finished?.Invoke(this.PointToScreen(e.GetPosition(this))); break;
            case MouseButton.Right: Finished?.Invoke(null); break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finished?.Invoke(null);
    }
}
