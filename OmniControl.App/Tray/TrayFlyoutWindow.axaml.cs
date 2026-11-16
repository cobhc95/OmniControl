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
/// windows, so it stays open while one is up. Sliders and tiles are dragged to another place by the shared <see cref="PanelDragReorder"/>
/// (Esc ends a drag before it hides the panel). Mouse first for the crosses, the drag and the right-click menu (AGENTS.md rule 5).
/// </summary>
public partial class TrayFlyoutWindow : Window
{
    private PixelPoint _click;
    private bool _picking;
    private bool _closing;
    private Flyout? _openMenu;
    private readonly PanelDragReorder _sliderDrag, _tileDrag;

    public TrayFlyoutWindow()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => Place();
        ScalingChanged += (_, _) => Place();
        _sliderDrag = new PanelDragReorder(SliderItems, vertical: true, CanDrag, item => item is TraySliderItem, (from, to) => Model?.Move(true, from, to));
        _tileDrag = new PanelDragReorder(TileItems, vertical: false, CanDrag, item => item is TrayTileItem, (from, to) => Model?.Move(false, from, to));
    }

    private bool CanDrag() => Model is { IsLocked: false };

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
        if (!_sliderDrag.Cancel() && !_tileDrag.Cancel()) HideNow();
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

    // Filled before it opens, so it never shows empty for a frame.
    private void OnItemMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: MenuRowList rows } menu || Model is not { } model) return;
        if (menu.Target?.DataContext is not ITrayPanelItem item) return;
        OpenMenu(menu, rows, model.ItemMenu(item, ChangeAction, ChangeIcon));
    }

    // The menu closes first (its row was clicked), then the picker opens over the panel.
    private void ChangeAction(ITrayPanelItem item) => Dispatcher.UIThread.Post(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.ChangePicker(item);
        if (await PickAsync(title, picker) is { } pick) model.Replace(item, pick);
    });

    private void ChangeIcon(ITrayPanelItem item) => Dispatcher.UIThread.Post(async () =>
    {
        if (Model is not { } model) return;
        if (await WhilePicking(() => model.ChooseIconAsync(this, item)) is { } pick) model.SetIcon(item, pick);
    });

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
    private Task<PickResult?> PickAsync(string title, PickerModel picker) => WhilePicking(() => PickerWindow.ChooseAsync(this, title, picker));

    /// <summary>A picker window over the flyout (it stays open meanwhile and has the keyboard again after it).</summary>
    private async Task<T?> WhilePicking<T>(Func<Task<T?>> pick) where T : class
    {
        _picking = true;
        try
        {
            return await pick();
        }
        finally
        {
            _picking = false;
            Activate();
        }
    }
}
