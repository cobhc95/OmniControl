namespace OmniControl.App.Services;

/// <summary>
/// Window-level requests that pages and hotkeys can make without knowing about the window or the tray icon.
/// App.axaml.cs owns the listeners.
/// </summary>
public static class AppLifecycle
{
    public static event Action? ShowRequested;
    public static event Action? QuitRequested;
    public static event Action<bool>? TrayEnabledChanged;

    public static void RequestShow() => ShowRequested?.Invoke();
    public static void RequestQuit() => QuitRequested?.Invoke();
    public static void SetTrayEnabled(bool on) => TrayEnabledChanged?.Invoke(on);
}
