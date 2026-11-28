using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Controls.ApplicationLifetimes;
using OmniControl.Core.Engine.Mouse;
using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.App.Features.Tray;

/// Layout and wiring only: the tray flyout's window. It opens beside the tray at the click (<see cref="TrayPlacement"/>, on the screen and
/// at the DPI of the click, placed again whenever its size or scaling changes), hides on an outside press or on
/// Esc, and is hidden rather than closed until the app ends. Tab and Shift+Tab go round its controls.
/// The pickers it opens are its own windows, so it stays open while one is up. Sliders and tiles are dragged to another place by the shared <see cref="PanelDragReorder"/>
/// (Esc ends a drag before it hides the panel). Mouse first for the crosses, the drag and the right-click menu.
public partial class TrayFlyoutWindow : Window
{
    private PixelPoint _click;
    private bool _picking;
    private bool _closing;
    private bool _listening;
    private int _showing;
    private Flyout? _openMenu;
    private readonly PanelDragReorder _sliderDrag, _tileDrag;

    public TrayFlyoutWindow()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Deactivated += (_, _) =>
        {
            if (!IsVisible || _picking) return;
            var showing = _showing;
            var switching = KeyboardState.SwitchingWindows();
            Dispatcher.UIThread.Post(() => { if (showing == _showing) CheckFocus(switching); }, DispatcherPriority.Background);
        };
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

    internal event Action<int, int>? OutsideLeftPress;

    /// When an outside press last dismissed the panel (its release must not reopen it).
    public DateTime HiddenAt { get; private set; } = DateTime.MinValue;

    /// Shows the flyout beside the tray icon clicked at <paramref name="click"/> (screen pixels) and gives it the keyboard; <paramref name="fromKeyboard"/>: opened by its hotkey, the first item is highlighted.
    public void ShowAt(PixelPoint click, bool fromKeyboard = false)
    {
        _click = click;
        var showing = ++_showing;
        Model?.Showing();
        if (!IsVisible)
        {
            Measure(Size.Infinity);
            Place(DesiredSize);
            Show();
            Win32Properties.AddWndProcHookCallback(this, OnWindowMessage);
            _listening = Guard.Run("Tray panel outside press", () => OutsidePress.Listen(TryGetPlatformHandle()!.Handle, true)) is null;
        }
        Place();
        Activate();
        Dispatcher.UIThread.Post(() =>
        {
            if (showing != _showing || !IsVisible || !IsActive || _picking || _openMenu is { IsOpen: true }) return;
            if (fromKeyboard) Model?.FocusFirst();
            else KeyboardNavigationHandler.GetNext(this, NavigationDirection.Next)?.Focus(NavigationMethod.Tab);
        }, fromKeyboard ? DispatcherPriority.Background : DispatcherPriority.Normal);
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
        Model?.Warm();
        (Opacity, ShowActivated) = (0, false);
        Show();
        Hide();
        (Opacity, ShowActivated) = (1, true);
    }

    /// Hides it as a click elsewhere does.
    public void Dismiss() { if (!_picking) HideNow(); }

    /// Ends it for good (the app is exiting).
    public void CloseForGood()
    {
        _closing = true;
        if (IsVisible) HideNow();
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
        Win32Properties.RemoveWndProcHookCallback(this, OnWindowMessage);
        if (_listening) Guard.Run("Tray panel outside press", () => OutsidePress.Listen(0, false));
        _listening = false;
        _openMenu?.Hide();
        Hide();
        Model?.Hidden();
    }

    private nint OnWindowMessage(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_picking && OutsidePress.FromMessage(window, message, lParam) is { } press
            && !new Rect(Bounds.Size).Contains(this.PointToClient(new PixelPoint(press.X, press.Y))))
        {
            HiddenAt = DateTime.UtcNow;
            HideNow();
            if (press.Left) OutsideLeftPress?.Invoke(press.X, press.Y);
        }
        return 0;
    }

    private void CheckFocus(bool switching)
    {
        if (!IsVisible || IsActive || _picking || OwnedWindows.Any(w => w.IsVisible)) return;
        // Opening another OmniControl window is deliberate; Explorer, tooltips and the secure desktop can take focus without a click.
        if (switching || !_listening || (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.Windows.Any(w => w.IsActive))) HideNow();
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
        OpenMenu(menu, rows, model.ItemMenu(item, ChangeAction, ChangeIcon, RenameTile));
    }

    // The menu closes first (its row was clicked), then the picker opens over the panel.
    private void ChangeAction(ITrayPanelItem item) => Defer(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.ChangePicker(item);
        if (await PickerWindow.ChooseAsync(this, title, picker) is { } pick) model.Replace(item, pick);
    });

    private void ChangeIcon(ITrayPanelItem item) => Defer(async () =>
    {
        if (Model is not { } model) return;
        if (await model.ChooseIconAsync(this, item) is { } pick) model.SetIcon(item, pick);
    });

    private void RenameTile(TrayTileItem item) => Defer(async () =>
    {
        if (Model is { } model && await DialogService.AskTextAsync(model.RenameQuestion(item), this) is { } name)
            model.Rename(item, name);
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
    private void OnAddSliderClick(object? sender, RoutedEventArgs e) => Defer(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.SliderPicker();
        if (await PickerWindow.ChooseAsync(this, title, picker) is { } pick) model.AddSlider(pick);
    });

    private void OnAddTileClick(object? sender, RoutedEventArgs e) => Defer(async () =>
    {
        if (Model is not { } model) return;
        var (title, picker) = model.TilePicker();
        if (await PickerWindow.ChooseAsync(this, title, picker) is { } pick) model.AddTile(pick);
    });

    /// Runs a picker window over the flyout, which stays open meanwhile and has the keyboard again after it. The flyout is marked as
    /// picking at once, before the posted work runs, because the closing menu or the click itself can already take the focus from it.
    private void Defer(Func<Task> pick)
    {
        if (_picking || !IsVisible) return;
        _picking = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Guard.RunAsync("Tray panel picker", pick);
            }
            finally
            {
                _picking = false;
                if (IsVisible && !_closing) Activate();
            }
        });
    }
}
