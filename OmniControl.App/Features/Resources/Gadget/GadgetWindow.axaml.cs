using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace OmniControl.App.Features.Resources.Gadget;

/// Layout and wiring only: draws the gadget's cells, the hover edge, tells a click from a double-click and opens the right-click menu.
public partial class GadgetWindow : Window
{
    private readonly Func<IReadOnlyList<MenuRowItem>> _menuRows;
    private IDisposable? _pendingClick;

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

    public event Action? Clicked;
    public event Action? DoubleClicked;

    /// The window's size in physical pixels (what the taskbar placement works in).
    public (int Width, int Height) PixelSize => ((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));

    public void Draw(IReadOnlyList<GadgetCell> cells, GadgetLayout layout) => Cells.Draw(cells, layout);

    private void OnPointerEntered(object? sender, PointerEventArgs e) => Frame.IsVisible = HoverOutline;

    private void OnPointerExited(object? sender, PointerEventArgs e) => Frame.IsVisible = false;

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        _pendingClick?.Dispose();
        var wait = PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.Zero;
        _pendingClick = DispatcherTimer.RunOnce(() => Clicked?.Invoke(), wait);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        _pendingClick?.Dispose();
        DoubleClicked?.Invoke();
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        Menu.ItemsSource = _menuRows();
        FlyoutBase.ShowAttachedFlyout(Root);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _pendingClick?.Dispose();
        base.OnClosed(e);
    }
}
