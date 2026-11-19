namespace OmniControl.App.Shared.Services;

/// Window-level requests that pages and hotkeys can make without knowing about the window or the tray icon.
/// App.axaml.cs owns the listeners. A request to show the window names its source, which is logged when the window really opens
/// (so a window that "opened by itself" can be traced).
public static class AppLifecycle
{
    public static event Action<string>? ShowRequested;
    public static event Action<string>? ToggleRequested;
    public static event Action? QuitRequested;

    /// Exit and start again (after an import or a wipe, which is carried out as the new run starts).
    public static event Action? RestartRequested;

    /// <param name="source">What asked, in words for the log ("a double-click on the tray icon").</param>
    public static void RequestShow(string source) => ShowRequested?.Invoke(source);
    /// Hide the window to the tray when it is the one in front, otherwise show and focus it.
    public static void RequestToggle(string source) => ToggleRequested?.Invoke(source);
    public static void RequestQuit() => QuitRequested?.Invoke();
    public static void RequestRestart() => RestartRequested?.Invoke();
}
