using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace OmniControl.App.Features.Resources.Gadget;

/// Layout and wiring only: draws the gadget's cells, the hover edge, reports presses, clicks and double-clicks and opens the right-click menu.
public partial class GadgetWindow : Window
{
    private readonly Func<IReadOnlyList<MenuRowItem>> _menuRows;

    public GadgetWindow() : this(Array.Empty<MenuRowItem>) { } // the designer only

    /// <param name="menuRows">The right-click menu's rows, asked each time it opens.</param>
    public GadgetWindow(Func<IReadOnlyList<MenuRowItem>> menuRows)
    {
        InitializeComponent();
        _menuRows = menuRows;
        Menu.Picked += (_, _) => (FlyoutBase.GetAttachedFlyout(Root) as Flyout)?.Hide();
    }

    /// The dashed edge shows while the pointer is over the gadget.
    public bool HoverOutline { get; set; }

    /// The colour that fills the whole gadget (opaque), or null to leave it clear.
    public void Fill(Color? colour) => Root.Background = colour is { } c ? new SolidColorBrush(c) : null;

    /// The left button went down, at this place in screen pixels.
    public event Action<PixelPoint>? Pressed;
    /// The left button came up (no tap-size check: a few pixels of movement still click).
    public event Action? Released;

    /// The window's size in physical pixels (what the taskbar placement works in).
    public (int Width, int Height) PixelSize => ((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));

    public void Draw(IReadOnlyList<GadgetCell> cells, GadgetLayout layout) => Cells.Draw(cells, layout);

    /// The hover edge came or went (the layered window shows a new picture only when given one).
    public event Action? EdgeChanged;

    /// Draws the window into <paramref name="bits"/>: premultiplied BGRA, <paramref name="width"/> x <paramref name="height"/> physical pixels,
    /// rows of width * 4 bytes (the layered taskbar window's picture).
    public void DrawInto(IntPtr bits, int width, int height)
    {
        var dpi = 96 * RenderScaling;
        using var picture = new RenderTargetBitmap(new PixelSize(width, height), new Vector(dpi, dpi));
        picture.Render(this);
        picture.CopyPixels(new PixelRect(0, 0, width, height), bits, width * height * 4, width * 4);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e) => ShowEdge(HoverOutline);

    private void OnPointerExited(object? sender, PointerEventArgs e) => ShowEdge(false);

    private void ShowEdge(bool on)
    {
        if (Frame.IsVisible == on) return;
        Frame.IsVisible = on;
        EdgeChanged?.Invoke();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) Pressed?.Invoke(this.PointToScreen(e.GetPosition(this)));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) Released?.Invoke();
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        Menu.ItemsSource = _menuRows();
        FlyoutBase.ShowAttachedFlyout(Root);
        e.Handled = true;
    }
}
