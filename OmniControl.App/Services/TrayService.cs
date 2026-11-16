using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace OmniControl.App.Services;

/// <summary>The notification-area icon: click or "Open" brings the window back, "Exit" exits for real.</summary>
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;

    private readonly NativeMenuItem _pauseItem;

    /// <param name="togglePause">Pauses all automation rules, or lets them work again.</param>
    public TrayService(Application app, Action open, Action exit, Action togglePause)
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open OmniControl");
        openItem.Click += (_, _) => open();
        _pauseItem = new NativeMenuItem(PauseText(paused: false));
        _pauseItem.Click += (_, _) => togglePause();
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(openItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OmniControl/Assets/OmniControl.ico"))),
            ToolTipText = "OmniControl",
            Menu = menu,
            IsVisible = true,
        };
        _icon.Clicked += (_, _) => open();
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
    }

    /// <summary>The menu item names what it will do next.</summary>
    public void ShowPaused(bool paused) => _pauseItem.Header = PauseText(paused);

    private static string PauseText(bool paused) => paused ? "Resume rules" : "Pause all rules";

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
