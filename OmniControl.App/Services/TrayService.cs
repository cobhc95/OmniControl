using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace OmniControl.App.Services;

/// <summary>The notification-area icon: click or "Open" brings the window back, "Exit" exits for real.</summary>
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;

    public TrayService(Application app, Action open, Action exit)
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open OmniControl");
        openItem.Click += (_, _) => open();
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(openItem);
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

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
