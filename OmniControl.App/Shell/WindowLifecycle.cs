using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using OmniControl.App.Services;

namespace OmniControl.App.Shell;

/// <summary>
/// How the main window comes and goes: the tray icon, hiding to the tray on close, starting minimised, and quitting.
/// Everything here is about the window's lifetime; what the window shows is <see cref="ShellViewModel"/>.
/// </summary>
public static class WindowLifecycle
{
    public static void Attach(Application app, IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window, ShellViewModel shell, AppSettings settings)
    {
        var quitting = false;
        void Quit() { quitting = true; desktop.Shutdown(); }
        void Show()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        var tray = new TrayService(app, Show, Quit) { Visible = settings.CloseToTray };

        // X hides the window to the tray (unless the user opted out, or the program is really exiting).
        window.Closing += (_, e) =>
        {
            if (quitting || !settings.CloseToTray || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            window.Hide();
        };

        if (settings.StartMinimised)
        {
            window.WindowState = WindowState.Minimized;
            if (settings.CloseToTray) window.Opened += (_, _) => window.Hide();
        }

        AppLifecycle.ShowRequested += Show;
        AppLifecycle.QuitRequested += Quit;
        AppLifecycle.TrayEnabledChanged += on =>
        {
            tray.Visible = on;
            if (!on) Show(); // never leave the window hidden with no tray icon to get it back
        };
        desktop.Exit += (_, _) => { tray.Dispose(); shell.Dispose(); };
    }
}
