using Avalonia.Controls.ApplicationLifetimes;
using OmniControl.App.Features.Tray;

namespace OmniControl.App.Shell;

/// How the main window comes and goes: the tray icon, what the X button does, starting hidden, exiting, and letting the window go after a
/// long hide (made again at the next showing, where it stood and at the page it showed). What the window shows is <see cref="ShellViewModel"/>.
public static class WindowLifecycle
{
    /// <param name="make">Makes a main window (the first one at start-up, a new one after a release).</param>
    /// <param name="startHidden">Start in the tray (started by the start-up entry): no window is made until it is first asked for.</param>
    public static void Attach(Application app, IClassicDesktopStyleApplicationLifetime desktop,
        Func<MainWindow> make, ShellViewModel shell, AppSettings settings, bool startHidden)
    {
        var quitting = false;
        var asking = false;
        var releasing = false; // the window is closed for good to give its memory back, not by the X button
        // The one exit path: the tray's Quit (the app.quit action), the footer button, the X button's "Exit" and the close dialog all end here,
        // and desktop.Exit below disposes the shell (engine, journal, display ramps).
        var ours = false; // true while Show or Hide below changes the window, so the change watcher knows it was asked for
        MainWindow? window = null;
        var seen = false; // the window in use has been on screen (not only started minimised in the tray)
        MainWindow.Placement? left = null; // where a released window stood, for the next one
        void Exit() { quitting = true; desktop.Shutdown(); }

        // Every open and every hide names what asked; when the window really opens or goes (it was hidden or minimised, or shown) that goes
        // to the errors log, so a window that "opened or closed by itself" can be traced.
        void Show(string source)
        {
            var shown = window ?? Make();
            if (!shown.IsVisible || shown.WindowState == WindowState.Minimized) Log($"Main window opened by {source}.");
            ours = true;
            try
            {
                shown.Show();
                if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
                shown.Activate();
            }
            finally { ours = false; }
        }
        void Hide(string source)
        {
            if (window is not { } shown) return;
            if (shown.IsVisible) Log($"Main window hidden by {source}.");
            ours = true;
            try { shown.Hide(); }
            finally { ours = false; }
        }

        void Apply(CloseBehavior behavior)
        {
            if (behavior == CloseBehavior.Exit) Exit();
            else Hide("the X button (When closing: keep running in the tray)");
        }

        void Ask(MainWindow owner)
        {
            if (asking) return;
            asking = true;
            Guard.Fire("Close choice", async () =>
            {
                try
                {
                    var choice = await new CloseChoiceWindow().ShowDialog<CloseChoice?>(owner);
                    if (choice is null) return;
                    if (choice.Remember)
                    {
                        settings.CloseBehavior = choice.Behavior;
                        settings.Save();
                    }
                    Apply(choice.Behavior);
                }
                finally { asking = false; }
            });
        }

        // A window and its wiring; the shell's page shows in it as soon as it is laid out.
        MainWindow Make()
        {
            var made = window = make();
            seen = false;
            desktop.MainWindow = made;
            if (left is { } placed) made.PlaceAs(placed);
            // X does what the setting says (a real exit, from any path, and a release are not intercepted).
            made.Closing += (_, e) =>
            {
                if (quitting || releasing || e.CloseReason != WindowCloseReason.WindowClosing) return;
                e.Cancel = true;
                if (settings.CloseBehavior == CloseBehavior.Ask) Dispatcher.UIThread.Post(() => Ask(made));
                else Dispatcher.UIThread.Post(() => Apply(settings.CloseBehavior));
            };
            // Tell the shell whether its page can be seen, so on-screen work (the gesture margins) runs only then.
            var started = false;
            made.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
                {
                    var onScreen = made.IsVisible && made.WindowState != WindowState.Minimized;
                    seen |= onScreen;
                    shell.SetWindowShown(onScreen, hidden: !made.IsVisible);
                }
                // Shown, hidden or minimised by anything but Show and Hide above (and the start-up itself): logged too, so the next report says
                // what did it (the minimise button and Windows' own Win+D or Win+M are the expected ones).
                if (!started || ours) return;
                if (e.Property == Visual.IsVisibleProperty)
                    Log(made.IsVisible ? "Main window opened, not by any of OmniControl's open requests." : "Main window hidden, not by any of OmniControl's hide requests.");
                else if (e.Property == Window.WindowStateProperty && made.WindowState == WindowState.Minimized)
                    Log("Main window minimised by the minimise button or Windows (not by OmniControl).");
            };
            made.Opened += (_, _) => Dispatcher.UIThread.Post(() => started = true);
            return made;
        }

        // Hidden a long while (the shell's timer): the window and its page view go, unless it or a window it owns (an FX window) is on
        // screen or a question is open; then it says why, and the shell asks again later. Seen at least once, the next window opens where this one stood.
        string? Release()
        {
            if (window is not { } hidden || quitting) return null;
            if (hidden.IsVisible) return "it is on screen or minimised";
            if (asking) return "the close question is open";
            if (hidden.OwnedWindows.FirstOrDefault(w => w.IsVisible) is { } owned) return $"a window it owns is open ({owned.Title})";
            if (seen) left = hidden.Placed;
            window = null;
            desktop.MainWindow = null;
            releasing = true;
            try { hidden.Close(); }
            finally { releasing = false; }
            Log("Main window let go after a long hide (made again at the next opening).");
            var gone = new WeakReference<MainWindow>(hidden);
            DispatcherTimer.RunOnce(() => UiTiming.Memory("after the main window was let go", gone.TryGetTarget(out _)
                ? "the let-go main window is still in memory (held by something, or not collected yet)" : "the let-go main window was collected"), Timing.UiReleaseLeakCheck);
            return null;
        }

        // Starting in the tray builds no main window: the first request to show it makes it, as after a release.
        if (startHidden || settings.StartMinimised)
            Log($"Main window not made: starting in the tray ({(startHidden ? "the start-up entry" : "Start minimised")}); made at its first opening.");
        else Make();
        var tray = new TrayService(app, () => Show("a double-click on the tray icon"), shell.TrayFlyout, shell.RunAction, shell.Hardware);
        shell.ReleaseWindows += Release; // the tray panel is never let go: it must open at once

        AppLifecycle.ShowRequested += Show;
        // A held hotkey repeats: each repeat is a request, and without this the window flipped shown, hidden, shown... while the key was down.
        var lastToggle = long.MinValue / 2;
        AppLifecycle.ToggleRequested += source =>
        {
            var now = Environment.TickCount64;
            var repeat = now - lastToggle < Timing.WindowToggleRepeat.TotalMilliseconds;
            lastToggle = now;
            if (repeat) return;
            if (window is { IsVisible: true, IsActive: true } open && open.WindowState != WindowState.Minimized) Hide(source);
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
