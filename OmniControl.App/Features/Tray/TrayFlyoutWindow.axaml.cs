using Avalonia.Input;
using Avalonia.VisualTree;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Tray;

/// Layout and wiring only: the tray flyout's window. It opens beside the tray at the click (<see cref="TrayPlacement"/>, on the screen and
/// at the DPI of the click, placed again whenever its size or scaling changes), hides when it loses the focus (a click anywhere else) or on
/// Esc, and is hidden rather than closed until the app ends. Tab and Shift+Tab go round its controls.
/// The pickers it opens are its own windows, so it stays open while one is up. Sliders and tiles are dragged to another place by the shared <see cref="PanelDragReorder"/>
/// (Esc ends a drag before it hides the panel). Mouse first for the crosses, the drag and the right-click menu.
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
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Wire(Model);
        Closed += (_, _) => Wire(null); // ended for good: the model no longer holds this window
        _sliderDrag = new PanelDragReorder(SliderItems, vertical: true, CanDrag, item => item is TraySliderItem, (from, to) => Model?.Move(true, from, to));
        _tileDrag = new PanelDragReorder(TileItems, vertical: false, CanDrag, item => item is TrayTileItem, (from, to) => Model?.Move(false, from, to));
    }

    private bool CanDrag() => Model is { IsLocked: false };

    private TrayFlyoutModel? Model => DataContext as TrayFlyoutModel;

    /// When it last hid itself (the tray icon's next click within <see cref="Shared.Services.Timing.TrayReopenGrace"/> only closed it).
    public DateTime HiddenAt { get; private set; } = DateTime.MinValue;

    /// Shows the flyout beside the tray icon clicked at <paramref name="click"/> (screen pixels) and gives it the keyboard; <paramref name="fromKeyboard"/>: opened by its hotkey, the first item is highlighted.
    public void ShowAt(PixelPoint click, bool fromKeyboard = false)
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
        if (fromKeyboard) Dispatcher.UIThread.Post(() => Model?.FocusFirst(), DispatcherPriority.Background);
        else Dispatcher.UIThread.Post(() => KeyboardNavigationHandler.GetNext(this, NavigationDirection.Next)?.Focus(NavigationMethod.Tab));
    }

    /// Whether the panel is on screen now.
    public bool IsOpen => IsVisible;

    // ---- keyboard (decided by the model) ----

    private TrayFlyoutModel? _wired;

    private void Wire(TrayFlyoutModel? model)
    {
        if (_wired is not null) _wired.FocusRequested -= OnFocusRequested;
        _wired = model;
        if (_wired is not null) _wired.FocusRequested += OnFocusRequested;
    }

    // Before the focused control sees the key (a slider would take the arrows itself), so the model's reading order is the only one.
    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model) return;
        var focused = FocusManager?.GetFocusedElement();
        model.Current = model.Of((focused as StyledElement)?.DataContext) ?? FootOf(focused);
        model.TileColumns = TileColumns();
        var result = model.HandleKey(e.Key, e.KeyModifiers);
        if (result == TrayKeyResult.Ignored) return;
        e.Handled = true;
        if (result == TrayKeyResult.OpenMenu && focused is Control { ContextFlyout: { } menu } control) menu.ShowAt(control);
    }

    private TrayFocus? FootOf(object? focused)
    {
        var index = Array.IndexOf(FootButtons, focused);
        return index < 0 ? null : new TrayFocus(TrayPart.Foot, index);
    }

    // In the order the model counts them (TrayFlyoutModel.FootButtons).
    private Button[] FootButtons => [AddTileButton, AddSliderButton, EditButton, OpenButton];

    /// How many tiles share the first row of the grid now.
    private int TileColumns()
    {
        var first = TileItems.ContainerFromIndex(0);
        return first is null ? 1 : Math.Max(1, TileItems.GetRealizedContainers().Count(c => c.Bounds.Y == first.Bounds.Y));
    }

    private void OnFocusRequested(TrayFocus at)
    {
        Control? target = at.Part switch
        {
            TrayPart.Slider => SliderItems.ContainerFromIndex(at.Index)?.GetVisualDescendants().OfType<Slider>().FirstOrDefault(),
            TrayPart.Tile => TileItems.ContainerFromIndex(at.Index)?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("tile")),
            _ => FootButtons[Math.Clamp(at.Index, 0, FootButtons.Length - 1)],
        };
        target?.Focus(NavigationMethod.Tab);
    }

    /// Builds the window's visuals once, unseen, so the first click only shows it.
    public void Warm()
    {
        Model?.Showing();
        (Opacity, ShowActivated) = (0, false);
        Show();
        Hide();
        (Opacity, ShowActivated) = (1, true);
        Model?.Hidden();
    }

    /// Hides it as a click elsewhere does.
    public void Dismiss() => HideNow();

    /// Ends it for good (the app is exiting).
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

    // Posted like the menu's pickers, so the click has finished before the picker window takes the keyboard.
    private void OnAddSliderClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.SliderPicker();
        if (await PickAsync(title, picker) is { } pick) model.AddSlider(pick);
    });

    private void OnAddTileClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.TilePicker();
        if (await PickAsync(title, picker) is { } pick) model.AddTile(pick);
    });

    /// The Choose-the-action window over the flyout, which stays open meanwhile and has the keyboard again after it.
    private Task<PickResult?> PickAsync(string title, PickerModel picker) => WhilePicking(() => PickerWindow.ChooseAsync(this, title, picker));

    /// A picker window over the flyout (it stays open meanwhile and has the keyboard again after it).
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
