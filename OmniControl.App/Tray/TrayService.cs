using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Tray;

/// <summary>
/// The notification-area icon (docs/mockups/tray-flyout.html): a single click opens the flyout beside it (<see cref="TrayFlyoutWindow"/>),
/// a double click opens OmniControl (<see cref="TrayClicks"/> keeps the single click from firing on a double click's first click), and a
/// right click opens its menu, whose items are master actions by id (<see cref="TrayDefaults.Menu"/>, a toggle ticked by its state).
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly TrayFlyoutModel _flyout;
    private readonly Func<string, bool> _run;
    private readonly TrayClicks _clicks;
    private TrayFlyoutWindow? _window;
    private PixelPoint _click;

    /// <param name="open">Shows the main window.</param>
    /// <param name="flyout">What the flyout and the menu show.</param>
    /// <param name="run">Runs a master action by id (the menu's items).</param>
    public TrayService(Application app, Action open, TrayFlyoutModel flyout, Func<string, bool> run)
    {
        (_flyout, _run) = (flyout, run);
        _clicks = new TrayClicks(ShowFlyout, open);
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
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        // The press on the icon took the focus from an open flyout, which hid itself: this click only closed it.
        if (_window is { } window && Grace.Within(window.HiddenAt, Timing.TrayReopenGrace)) return;
        if (SystemCommands.GetCursorPos() is { } at) _click = new PixelPoint(at.X, at.Y);
        _clicks.Click();
    }

    private void ShowFlyout()
    {
        _window ??= new TrayFlyoutWindow { DataContext = _flyout };
        _window.ShowAt(_click);
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
        _clicks.Dispose();
        _window?.CloseForGood();
        _icon.Clicked -= OnClicked;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
