namespace OmniControl.App.Services;

/// <summary>
/// Window-level requests that pages and hotkeys can make without knowing about the window or the tray icon.
/// App.axaml.cs owns the listeners.
/// </summary>
public static class AppLifecycle
{
    public static event Action? ShowRequested;
    public static event Action? QuitRequested;

    /// <summary>Exit and start again (after an import or a wipe, which is carried out as the new run starts).</summary>
    public static event Action? RestartRequested;

    public static void RequestShow() => ShowRequested?.Invoke();
    public static void RequestQuit() => QuitRequested?.Invoke();
    public static void RequestRestart() => RestartRequested?.Invoke();
}
