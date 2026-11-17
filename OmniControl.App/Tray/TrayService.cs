using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Tray;

/// <summary>
/// The notification-area icon (docs/mockups/tray-flyout.html): a single click opens the flyout beside it at once (<see cref="TrayFlyoutWindow"/>, built hidden
/// after start-up), a second click within the double-click time closes it and opens OmniControl (<see cref="TrayClicks"/>), and a
/// right click opens its menu, whose items are master actions by id (<see cref="TrayDefaults.Menu"/>, a toggle ticked by its state).
/// </summary>
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
    public TrayService(Application app, Action open, TrayFlyoutModel flyout, Func<string, bool> run)
    {
        (_open, _flyout, _run) = (open, flyout, run);
        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OmniControl/Assets/OmniControl.ico"))),
            ToolTipText = "OmniControl",
            IsVisible = true,
        };
        _icon.Clicked += OnClicked;
        _icon.Menu = new NativeMenu();
        FillMenu();
        _flyout.MenuChanged += FillMenu;
        _flyout.ToggleRequested += OnToggleRequested;
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
        if (!StartupMode.Inert) Dispatcher.UIThread.Post(Prebuild, DispatcherPriority.Background);
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        var timer = Stopwatch.StartNew();
        if (_clicks.IsDouble())
        {
            _window?.Dismiss(); // the first click opened it
            _open();
            return;
        }
        // The press on the icon took the focus from an open flyout, which hid itself: this click only closed it.
        if (_window is { } window && Grace.Within(window.HiddenAt, Timing.TrayReopenGrace)) return;
        if (SystemCommands.GetCursorPos() is { } at) _click = new PixelPoint(at.X, at.Y);
        ShowFlyout(timer);
    }

    /// <summary>The flyout is made once, hidden, so a click only shows it again.</summary>
    private TrayFlyoutWindow Window()
    {
        return _window ??= new TrayFlyoutWindow { DataContext = _flyout };
    }

    private void Prebuild() => Window().Warm();

    private void ShowFlyout(Stopwatch timer)
    {
        Window().ShowAt(_click);
        UiTiming.Shown("Tray panel", timer);
    }

    /// <summary>The panel's hotkey: closes it when it is open, else opens it above the notification area with the keyboard on its first item.</summary>
    private void OnToggleRequested()
    {
        if (Window() is { IsOpen: true } open)
        {
            open.Dismiss();
            return;
        }
        var at = TrayArea.Centre() ?? SystemCommands.GetCursorPos();
        if (at is { } point) _click = new PixelPoint(point.X, point.Y);
        Window().ShowAt(_click, fromKeyboard: true);
    }

    /// <summary>The menu again from the master list: one group after another with a thin line between, a toggle with its tick.</summary>
    private void FillMenu()
    {
        if (_icon.Menu is not { } menu) return;
        menu.Items.Clear();
        foreach (var group in _flyout.Menu)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new NativeMenuItemSeparator());
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

    public void Dispose()
    {
        _flyout.MenuChanged -= FillMenu;
        _flyout.ToggleRequested -= OnToggleRequested;
        _window?.CloseForGood();
        _icon.Clicked -= OnClicked;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
