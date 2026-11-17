namespace OmniControl.App.Services;

/// <summary>
/// Window-level requests that pages and hotkeys can make without knowing about the window or the tray icon.
/// App.axaml.cs owns the listeners.
/// </summary>
public static class AppLifecycle
{
    public static event Action? ShowRequested;
    public static event Action? ToggleRequested;
    public static event Action? QuitRequested;

    /// <summary>Exit and start again (after an import or a wipe, which is carried out as the new run starts).</summary>
    public static event Action? RestartRequested;

    public static void RequestShow() => ShowRequested?.Invoke();
    /// <summary>Hide the window to the tray when it is the one in front, otherwise show and focus it.</summary>
    public static void RequestToggle() => ToggleRequested?.Invoke();
    public static void RequestQuit() => QuitRequested?.Invoke();
    public static void RequestRestart() => RestartRequested?.Invoke();
}
