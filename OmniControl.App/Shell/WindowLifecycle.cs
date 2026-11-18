using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.App.Tray;

namespace OmniControl.App.Shell;

/// <summary>
/// How the main window comes and goes: the tray icon, what the X button does, starting hidden, and exiting.
/// Everything here is about the window's lifetime; what the window shows is <see cref="ShellViewModel"/>.
/// </summary>
public static class WindowLifecycle
{
    /// <param name="startHidden">Start in the tray: the window is built but not shown (started by the start-up entry).</param>
    public static void Attach(Application app, IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window, ShellViewModel shell, AppSettings settings, bool startHidden)
    {
        var quitting = false;
        var asking = false;
        // The one exit path: the tray's Quit (the app.quit action), the footer button, the X button's "Exit" and the close dialog all end here,
        // and desktop.Exit below disposes the shell (engine, journal, display ramps).
        var showing = false;
        void Exit() { quitting = true; desktop.Shutdown(); }
        // Every open names what asked; when the window really opens (it was hidden or minimised) that goes to the errors log, so a window
        // that "opened by itself" can be traced.
        void Show(string source)
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized) AppDataStore.AppendLog(Guard.LogFile, $"Main window opened by {source}.");
            showing = true;
            try
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }
            finally { showing = false; }
        }

        void Apply(CloseBehavior behavior)
        {
            if (behavior == CloseBehavior.Exit) Exit();
            else window.Hide();
        }

        async void Ask()
        {
            if (asking) return;
            asking = true;
            try
            {
                var choice = await new CloseChoiceWindow().ShowDialog<CloseChoice?>(window);
                if (choice is null) return;
                if (choice.Remember)
                {
                    settings.CloseBehavior = choice.Behavior;
                    settings.Save();
                }
                Apply(choice.Behavior);
            }
            finally { asking = false; }
        }

        var tray = new TrayService(app, () => Show("a double-click on the tray icon"), shell.TrayFlyout, shell.RunAction);

        // X does what the setting says (a real exit, from any path, is not intercepted).
        window.Closing += (_, e) =>
        {
            if (quitting || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            if (settings.CloseBehavior == CloseBehavior.Ask) Dispatcher.UIThread.Post(Ask);
            else Dispatcher.UIThread.Post(() => Apply(settings.CloseBehavior));
        };

        // Tell the shell whether its page can be seen, so on-screen work (the gesture margins) runs only then.
        var started = false;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
                shell.SetWindowShown(window.IsVisible && window.WindowState != WindowState.Minimized);
            // Hidden to shown by anything but Show above (and the start-up itself): logged too, as nothing else should open it.
            if (started && !showing && e.Property == Visual.IsVisibleProperty && window.IsVisible)
                AppDataStore.AppendLog(Guard.LogFile, "Main window opened, not by any of OmniControl's open requests.");
        };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => started = true);

        if (startHidden || settings.StartMinimised)
        {
            window.WindowState = WindowState.Minimized;
            window.Opened += (_, _) => window.Hide();
        }

        AppLifecycle.ShowRequested += Show;
        AppLifecycle.ToggleRequested += source =>
        {
            if (window.IsVisible && window.WindowState != WindowState.Minimized && window.IsActive) window.Hide();
            else Show(source);
        };
        AppLifecycle.QuitRequested += Exit;
        AppLifecycle.RestartRequested += () =>
        {
            AppRestart.StartNext();
            Exit();
        };
        desktop.Exit += (_, _) => { tray.Dispose(); shell.Dispose(); };
    }
}
