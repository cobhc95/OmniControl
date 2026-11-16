using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OmniControl.App.Controls;
using OmniControl.App.Picker;

namespace OmniControl.App.Tray;

/// <summary>
/// Layout and wiring only: the tray flyout's window. It opens beside the tray at the click (<see cref="TrayPlacement"/>, on the screen and
/// at the DPI of the click, placed again whenever its size or scaling changes), hides when it loses the focus (a click anywhere else) or on
/// Esc, and is hidden rather than closed until the app ends. Tab and Shift+Tab go round its controls. The pickers it opens are its own
/// windows, so it stays open while one is up. Mouse first in edit mode (the crosses, the right-click menu; AGENTS.md rule 5).
/// </summary>
public partial class TrayFlyoutWindow : Window
{
    private PixelPoint _click;
    private bool _picking;
    private bool _closing;
    private Flyout? _openMenu;

    public TrayFlyoutWindow()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => Place();
        ScalingChanged += (_, _) => Place();
    }

    private TrayFlyoutModel? Model => DataContext as TrayFlyoutModel;

    /// <summary>When it last hid itself (the tray icon's next click within <see cref="Services.Timing.TrayReopenGrace"/> only closed it).</summary>
    public DateTime HiddenAt { get; private set; } = DateTime.MinValue;

    /// <summary>Shows the flyout beside the tray icon clicked at <paramref name="click"/> (screen pixels) and gives it the keyboard.</summary>
    public void ShowAt(PixelPoint click)
    {
        _click = click;
        Model?.Showing();
        if (!IsVisible)
        {
            Measure(Size.Infinity);
            Place(DesiredSize);
            Show();
        }
        Place();
        Activate();
        Dispatcher.UIThread.Post(() => KeyboardNavigationHandler.GetNext(this, NavigationDirection.Next)?.Focus(NavigationMethod.Tab));
    }

    /// <summary>Builds the window's visuals once, unseen, so the first click only shows it.</summary>
    public void Warm()
    {
        Model?.Showing();
        (Opacity, ShowActivated) = (0, false);
        Show();
        Hide();
        (Opacity, ShowActivated) = (1, true);
        Model?.Hidden();
    }

    /// <summary>Hides it as a click elsewhere does.</summary>
    public void Dismiss() => HideNow();

    /// <summary>Ends it for good (the app is exiting).</summary>
    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    private void Place() => Place(Bounds.Size);

    private void Place(Size size)
    {
        if (!IsVisible && size == default) return;
        if ((Screens.ScreenFromPoint(_click) ?? Screens.Primary) is not { } screen) return;
        var gap = Application.Current?.TryFindResource("TrayFlyoutGap", out var value) == true && value is double d ? d : 0;
        Position = TrayPlacement.Place(_click, screen.WorkingArea, PixelSize.FromSize(size, screen.Scaling), (int)Math.Round(gap * screen.Scaling));
    }

    private void HideNow()
    {
        if (!IsVisible) return;
        _openMenu?.Hide();
        Hide();
        HiddenAt = DateTime.UtcNow;
        Model?.Hidden();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_picking) HideNow();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        HideNow();
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;
        e.Cancel = true; // Alt+F4 hides it like any click away
        HideNow();
    }

    // ---- pick lists ----

    private void OnChoiceMenuOpened(object? sender, Flyout menu)
    {
        if (sender is not ChipMenu { DataContext: TraySliderItem item, Menu: MenuRowList rows }) return;
        OpenMenu(menu, rows, item.ChoiceRows);
    }

    private void OnItemMenuOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: MenuRowList rows } menu || Model is not { } model) return;
        if (menu.Target?.DataContext is not ITrayPanelItem item) return;
        var target = menu.Target;
        OpenMenu(menu, rows, model.ItemMenu(item, icons => Dispatcher.UIThread.Post(() =>
        {
            OpenMenu(menu, rows, icons);
            menu.ShowAt(target);
        })));
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ITrayPanelItem item) Model?.Remove(item);
    }

    private void OpenMenu(Flyout menu, MenuRowList rows, IReadOnlyList<MenuRowItem> items)
    {
        _openMenu = menu;
        rows.Picked -= OnMenuPicked;
        rows.Picked += OnMenuPicked;
        rows.ItemsSource = items;
    }

    private void OnMenuPicked(object? sender, EventArgs e) => _openMenu?.Hide();

    // ---- pickers ----

    private async void OnAddSliderClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        var (title, picker) = model.SliderPicker();
        if (await PickAsync(title, picker) is { } pick) model.AddSlider(pick);
    }

    private async void OnAddTileClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        var (title, picker) = model.TilePicker();
        if (await PickAsync(title, picker) is { } pick) model.AddTile(pick);
    }

    /// <summary>The Choose-the-action window over the flyout, which stays open meanwhile and has the keyboard again after it.</summary>
    private async Task<PickResult?> PickAsync(string title, PickerModel picker)
    {
        _picking = true;
        try
        {
            return await PickerWindow.ChooseAsync(this, title, picker);
        }
        finally
        {
            _picking = false;
            Activate();
        }
    }
}
