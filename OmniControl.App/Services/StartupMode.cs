namespace OmniControl.App.Services;

/// <summary>
/// Whether this run may touch the system. Both levels exist only in a Debug build's screenshot export (<c>--export-screenshots</c>); in
/// Release every value is the normal one.
/// <see cref="Inert"/>: no hook, hotkey, watcher, timer, engine, device call or write to Windows may start.
/// <see cref="ReadOnly"/> (<c>--real-data</c>, on a copy of the owner's settings): still inert, but a page may look at the system and
/// show it (monitors, audio devices and programs, Windows' pointer and touchpad settings).
/// </summary>
internal static class StartupMode
{
    public static bool Inert { get; } =
#if DEBUG
        Environment.GetEnvironmentVariable(Showcase.ShowcaseOptions.DataFolderVariable) is not null;
#else
        false;
#endif

    public static bool ReadOnly { get; } =
#if DEBUG
        Inert && Environment.GetEnvironmentVariable(Showcase.ShowcaseOptions.RealDataVariable) is not null;
#else
        false;
#endif

    /// <summary>True when a read-only look at the system is allowed: a normal run, or a read-only export.</summary>
    public static bool MayRead => !Inert || ReadOnly;
}
