using System.Diagnostics;
using Avalonia.Media.Imaging;
using OmniControl.App.Features.Icons;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Tray;

/// The notification-area icon: a single click opens the flyout beside it (<see cref="TrayFlyoutWindow"/>, built at the first click and
/// let go after a long hide), a second press or click within the double-click time closes it and opens OmniControl (<see cref="TrayClicks"/>), and a
/// right click opens its menu, whose items are master actions by id (<see cref="TrayDefaults.Menu"/>, a toggle ticked by its state) and the
/// icon's look (<see cref="ITrayIconLook"/>), drawn again on a change of the look, the accent or the taskbar's theme.
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly TrayFlyoutModel _flyout;
    private readonly Func<string, bool> _run;
    private readonly Action _open;
    private readonly TrayClicks _clicks = new();
    private TrayFlyoutWindow? _window;
    private PixelPoint _click;

    /// <param name="open">Shows the main window.</param>
    /// <param name="flyout">What the flyout and the menu show.</param>
    /// <param name="run">Runs a master action by id (the menu's items).</param>
    /// <param name="hardware">Where its watch of the taskbar's mode goes (Avalonia itself adds the icon again when Explorer restarts).</param>
    public TrayService(Application app, Action open, TrayFlyoutModel flyout, Func<string, bool> run, IHardwareWatches hardware)
    {
        (_app, _open, _flyout, _run) = (app, open, flyout, run);
        _icon = new TrayIcon { ToolTipText = "OmniControl", IsVisible = true };
        DrawIcon();
        _icon.Clicked += OnClicked;
        _icon.Menu = new NativeMenu();
        FillMenu();
        _flyout.MenuChanged += FillMenu;
        _flyout.ToggleRequested += OnToggleRequested;
        _flyout.TrayIconChanged += OnLookChanged;
        AccentService.Changed += OnLookChanged;
        // Windows' mode alone (the taskbar's colour) changes no app colour, so Avalonia does not tell it: the setting change does.
        hardware.Register(new HardwareWatch(WatchId, "Tray icon", HardwareSignal.TaskbarSettings, () =>
        {
            var light = TaskbarSpot.IsLight(); // the registry is read here, off the UI thread
            Dispatcher.UIThread.Post(() => OnTaskbarMode(light));
        }));
        app.ActualThemeVariantChanged += OnThemeChanged;
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
    }

    private const string WatchId = "tray.icon";
    private readonly Application _app;
    private bool? _taskbarLight; // read once, and again after each change of Windows' mode
    private bool _disposed;
    private (string? Icon, Color? Ink)? _drawn;
    private List<Bitmap>? _dots; // the colour menu's dots, drawn when the menu is first built after a change

    private bool TaskbarLight => _taskbarLight ??= TaskbarSpot.IsLight();

    /// Draws the icon again only when what it shows changed (an icon that is gone shows the logo).
    private void DrawIcon()
    {
        var image = IconResources.Find(_flyout.TrayIcon);
        var icon = image is null ? null : _flyout.TrayIcon;
        var ink = TrayIconChoices.Ink(icon, _flyout.TrayIconColour, TaskbarLight);
        if (_drawn == (icon, ink)) return;
        _drawn = (icon, ink);
        _icon.Icon = TrayIconImage.Make(image, ink);
    }

    private void OnLookChanged()
    {
        ForgetDots();
        DrawIcon();
        FillMenu();
    }

    private void OnTaskbarMode(bool light)
    {
        if (_disposed) return;
        _taskbarLight = light;
        DrawIcon();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ForgetDots();
        FillMenu();
    }

    // Not disposed: the menu that was just clicked may still be drawing them as it closes; SkiaSharp frees them when collected.
    private void ForgetDots() => _dots = null;

    private void OnClicked(object? sender, EventArgs e)
    {
        var timer = Stopwatch.StartNew();
        var at = SystemCommands.GetCursorPos();
        // Checked first, so the second click of a double click never toggles the panel shut: it closes the panel and opens OmniControl.
        if (_clicks.Click(at))
        {
            _window?.Dismiss();
            _open();
            return;
        }
        // The press on the icon took the focus from an open flyout, which hid itself: this click only closed it.
        if (_window is { } window && Grace.Within(window.HiddenAt, Timing.TrayReopenGrace)) return;
        if (at is { } point) _click = new PixelPoint(point.X, point.Y);
        ShowFlyout(timer);
    }

    // Windows may send no click for the second press of a double click that takes the focus from the open panel (only the press reaches the
    // taskbar), so that press, seen as the panel hiding itself, is the double click; a release that still comes falls in TrayReopenGrace.
    private void OnPanelDeactivated(object? sender, EventArgs e)
    {
        if (_window is { IsOpen: false } && _clicks.IsSecond(SystemCommands.GetCursorPos())) Dispatcher.UIThread.Post(_open);
    }

    /// The flyout is made at its first showing and kept hidden between showings, so a click only shows it again.
    private TrayFlyoutWindow Window()
    {
        if (_window is null)
        {
            _window = new TrayFlyoutWindow { DataContext = _flyout };
            _window.Deactivated += OnPanelDeactivated;
        }
        return _window;
    }

    /// Lets the hidden flyout go (OmniControl has sat in the tray a long while): its views use no memory until the next showing makes it again.
    public void Release()
    {
        if (_window is not { IsOpen: false } window) return;
        window.Deactivated -= OnPanelDeactivated;
        window.CloseForGood();
        _window = null;
    }

    private void ShowFlyout(Stopwatch timer)
    {
        Window().ShowAt(_click);
        UiTiming.Shown("Tray panel", timer);
    }

    /// The panel's hotkey: closes it when it is open, else opens it above the notification area with the keyboard on its first item.
    private void OnToggleRequested()
    {
        if (Window() is { IsOpen: true } open)
        {
            open.Dismiss();
            return;
        }
        // A click on the gadget took the focus from the open panel, which hid itself: that click only closes it, as on the icon.
        if (Grace.Within(Window().HiddenAt, Timing.TrayReopenGrace)) return;
        var at = TrayArea.Centre() ?? SystemCommands.GetCursorPos();
        if (at is { } point) _click = new PixelPoint(point.X, point.Y);
        Window().ShowAt(_click, fromKeyboard: true);
    }

    /// The menu again from the master list: one group after another with a thin line between, a toggle with its tick.
    private void FillMenu()
    {
        if (_icon.Menu is not { } menu) return;
        menu.Items.Clear();
        var groups = _flyout.Menu;
        foreach (var group in groups)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new NativeMenuItemSeparator());
            if (group == groups[^1]) // the look's two sub-menus sit just before the last group (App settings, Quit)
            {
                menu.Items.Add(IconMenu());
                menu.Items.Add(ColourMenu());
                menu.Items.Add(new NativeMenuItemSeparator());
            }
            foreach (var target in group)
            {
                if (target.Action is not { } action) continue;
                var item = new NativeMenuItem(target.Name);
                if (target.State is not null)
                {
                    item.ToggleType = NativeMenuItemToggleType.CheckBox;
                    item.IsChecked = _flyout.IsOn(target);
                }
                item.Click += (_, _) => _run(action.Id);
                menu.Items.Add(item);
            }
        }
    }

    /// "Tray icon ›": the four icons, the one in use ticked, then "Choose any icon…".
    private NativeMenuItem IconMenu()
    {
        var menu = new NativeMenu();
        foreach (var (icon, name) in TrayIconChoices.Icons)
            menu.Items.Add(Item(name, () => _flyout.SetTrayIcon(icon), NativeMenuItemToggleType.Radio, _flyout.TrayIcon == icon));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(TrayIconChoices.ChooseWords, ChooseIcon));
        return new NativeMenuItem(TrayIconChoices.IconWords) { Menu = menu };
    }

    /// "Tray icon colour ›": one row per colour with its dot, the chosen dot ringed in the accent.
    private NativeMenuItem ColourMenu()
    {
        var choices = TrayIconChoices.Colours;
        _dots ??= choices.Select(c => TrayIconImage.Dot(TrayIconChoices.Fill(c.Colour), c.Colour == _flyout.TrayIconColour, c.Colour is null, MutedBrush())).ToList();
        var menu = new NativeMenu();
        for (var i = 0; i < choices.Count; i++)
        {
            var colour = choices[i].Colour;
            var item = Item(choices[i].Name, () => _flyout.SetTrayIconColour(colour));
            item.Icon = _dots[i];
            menu.Items.Add(item);
        }
        return new NativeMenuItem(TrayIconChoices.ColourWords) { Menu = menu };
    }

    /// The muted edge of the theme in use, so a white or black dot shows on either menu.
    private IBrush MutedBrush() =>
        _app.TryFindResource("BrushMuted", _app.ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;

    private static NativeMenuItem Item(string text, Action run, NativeMenuItemToggleType toggle = NativeMenuItemToggleType.None, bool ticked = false)
    {
        var item = new NativeMenuItem(text) { ToggleType = toggle, IsChecked = ticked };
        item.Click += (_, _) => run();
        return item;
    }

    /// "Choose any icon…": OmniControl comes forward and THE icon picker opens over it.
    private void ChooseIcon()
    {
        AppLifecycle.RequestShow("the tray menu's Choose any icon…");
        Dispatcher.UIThread.Post(() => _ = Guard.RunAsync("Tray icon picker", async () =>
        {
            if (OwnedWindow.MainWindow is { } window) await _flyout.ChooseTrayIconAsync(window);
        }));
    }

    public void Dispose()
    {
        _flyout.TrayIconChanged -= OnLookChanged;
        _disposed = true;
        AccentService.Changed -= OnLookChanged;
        _app.ActualThemeVariantChanged -= OnThemeChanged;
        _flyout.MenuChanged -= FillMenu;
        _flyout.ToggleRequested -= OnToggleRequested;
        if (_window is { } window)
        {
            window.Deactivated -= OnPanelDeactivated;
            window.CloseForGood();
        }
        _icon.Clicked -= OnClicked;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
