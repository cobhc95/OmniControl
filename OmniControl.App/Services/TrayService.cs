using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace OmniControl.App.Services;

/// <summary>The notification-area icon: click or "Open" brings the window back, "Quit" exits for real.</summary>
public sealed class TrayService : IDisposable
{
    private readonly TrayIcon _icon;

    public TrayService(Application app, Action open, Action quit)
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open OmniControl");
        openItem.Click += (_, _) => open();
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => quit();
        menu.Items.Add(openItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _icon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OmniControl/Assets/OmniControl.ico"))),
            ToolTipText = "OmniControl",
            Menu = menu,
            IsVisible = false,
        };
        _icon.Clicked += (_, _) => open();
        TrayIcon.SetIcons(app, new TrayIcons { _icon });
    }

    public bool Visible
    {
        get => _icon.IsVisible;
        set => _icon.IsVisible = value;
    }

    public void Dispose()
    {
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
