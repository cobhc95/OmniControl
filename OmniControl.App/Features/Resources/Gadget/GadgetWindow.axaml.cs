using Avalonia.Controls.Primitives;
using Avalonia.Input;

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

    /// The left button went down (true: the second press of a double-click).
    public event Action<bool>? Pressed;
    /// The button came up after a press without a drag.
    public event Action? Clicked;
    public event Action? DoubleClicked;

    /// Windows' double-click time.
    public TimeSpan DoubleClickTime => PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.Zero;

    /// The window's size in physical pixels (what the taskbar placement works in).
    public (int Width, int Height) PixelSize => ((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));

    public void Draw(IReadOnlyList<GadgetCell> cells, GadgetLayout layout) => Cells.Draw(cells, layout);

    private void OnPointerEntered(object? sender, PointerEventArgs e) => Frame.IsVisible = HoverOutline;

    private void OnPointerExited(object? sender, PointerEventArgs e) => Frame.IsVisible = false;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) Pressed?.Invoke(e.ClickCount > 1);
    }

    private void OnTapped(object? sender, TappedEventArgs e) => Clicked?.Invoke();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => DoubleClicked?.Invoke();

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        Menu.ItemsSource = _menuRows();
        FlyoutBase.ShowAttachedFlyout(Root);
        e.Handled = true;
    }
}
