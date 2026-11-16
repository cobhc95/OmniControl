using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using OmniControl.App.Services;

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
        // The one exit path: the tray's Exit, the footer button, the X button's "Exit" and the close dialog all end here,
        // and desktop.Exit below disposes the shell (engine, journal, display ramps).
        void Exit() { quitting = true; desktop.Shutdown(); }
        void Show()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
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

        var tray = new TrayService(app, Show, Exit, shell.ToggleRulesPause);
        void ShowPause() => tray.ShowPaused(shell.RulesPaused);
        shell.RulesChanged += ShowPause;

        // X does what the setting says (a real exit, from any path, is not intercepted).
        window.Closing += (_, e) =>
        {
            if (quitting || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            if (settings.CloseBehavior == CloseBehavior.Ask) Dispatcher.UIThread.Post(Ask);
            else Dispatcher.UIThread.Post(() => Apply(settings.CloseBehavior));
        };

        // Tell the shell whether its page can be seen, so on-screen work (the gesture margins) runs only then.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
                shell.SetWindowShown(window.IsVisible && window.WindowState != WindowState.Minimized);
        };

        // The desktop's displays changed (one plugged in or removed): pages that follow them look again. The window gets this even while hidden.
        window.Screens.Changed += (_, _) => shell.NotifyDisplaysChanged();

        if (startHidden || settings.StartMinimised)
        {
            window.WindowState = WindowState.Minimized;
            window.Opened += (_, _) => window.Hide();
        }

        AppLifecycle.ShowRequested += Show;
        AppLifecycle.QuitRequested += Exit;
        AppLifecycle.RestartRequested += () =>
        {
            AppRestart.StartNext();
            Exit();
        };
        desktop.Exit += (_, _) => { shell.RulesChanged -= ShowPause; tray.Dispose(); shell.Dispose(); };
    }
}
