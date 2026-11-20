using Avalonia.Controls.ApplicationLifetimes;
using OmniControl.App.Features.Tray;

namespace OmniControl.App.Shell;

/// How the main window comes and goes: the tray icon, what the X button does, starting hidden, and exiting.
/// Everything here is about the window's lifetime; what the window shows is <see cref="ShellViewModel"/>.
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
        var ours = false; // true while Show or Hide below changes the window, so the change watcher knows it was asked for
        void Exit() { quitting = true; desktop.Shutdown(); }
        // Every open and every hide names what asked; when the window really opens or goes (it was hidden or minimised, or shown) that goes
        // to the errors log, so a window that "opened or closed by itself" can be traced.
        void Show(string source)
        {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized) Log($"Main window opened by {source}.");
            ours = true;
            try
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            }
            finally { ours = false; }
        }
        void Hide(string source)
        {
            if (window.IsVisible) Log($"Main window hidden by {source}.");
            ours = true;
            try { window.Hide(); }
            finally { ours = false; }
        }

        void Apply(CloseBehavior behavior)
        {
            if (behavior == CloseBehavior.Exit) Exit();
            else Hide("the X button (When closing: keep running in the tray)");
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

        var tray = new TrayService(app, () => Show("a double-click on the tray icon"), shell.TrayFlyout, shell.RunAction, shell.Hardware);

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
            // Shown, hidden or minimised by anything but Show and Hide above (and the start-up itself): logged too, so the next report says
            // what did it (the minimise button and Windows' own Win+D or Win+M are the expected ones).
            if (!started || ours) return;
            if (e.Property == Visual.IsVisibleProperty)
                Log(window.IsVisible ? "Main window opened, not by any of OmniControl's open requests." : "Main window hidden, not by any of OmniControl's hide requests.");
            else if (e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
                Log("Main window minimised by the minimise button or Windows (not by OmniControl).");
        };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => started = true);

        if (startHidden || settings.StartMinimised)
        {
            // Avalonia raises Opened on EVERY show, not only the first: a handler that stayed subscribed hid the window again each time
            // it was opened (from the tray, a hotkey or another start), so it showed and at once went away. It runs once and lets go.
            var startSource = startHidden ? "starting in the tray (the start-up entry)" : "starting in the tray (Start minimised)";
            Log($"Main window minimised by {startSource}.");
            window.WindowState = WindowState.Minimized;
            void HideOnFirstOpen(object? sender, EventArgs e)
            {
                window.Opened -= HideOnFirstOpen;
                Hide(startSource);
            }
            window.Opened += HideOnFirstOpen;
        }

        AppLifecycle.ShowRequested += Show;
        // A held hotkey repeats: each repeat is a request, and without this the window flipped shown, hidden, shown... while the key was down.
        var lastToggle = long.MinValue / 2;
        AppLifecycle.ToggleRequested += source =>
        {
            var now = Environment.TickCount64;
            var repeat = now - lastToggle < Timing.WindowToggleRepeat.TotalMilliseconds;
            lastToggle = now;
            if (repeat) return;
            if (window.IsVisible && window.WindowState != WindowState.Minimized && window.IsActive) Hide(source);
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

    private static void Log(string line) => AppDataStore.AppendLog(Guard.LogFile, line);
}
