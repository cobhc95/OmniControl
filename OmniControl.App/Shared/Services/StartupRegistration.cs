namespace OmniControl.App.Shared.Services;

/// "Start with Windows": what OmniControl puts in the user's start-up list (name and command line defined here, the registry
/// work is <see cref="StartupEntry"/>). Windows is the source of truth; nothing is kept in settings.
public static class StartupRegistration
{
    private const string EntryName = "OmniControl";

    /// The argument the start-up entry passes: start with the window hidden, everything else running.
    private const string BackgroundArgument = "--background";

    public static StartupState State => StartupEntry.StateOf(EntryName);

    /// True when the program was started by its start-up entry.
    public static bool IsBackgroundStart(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, BackgroundArgument, StringComparison.OrdinalIgnoreCase));

    /// <returns>False when Windows refused the change.</returns>
    public static bool Set(bool on) => StartupEntry.Set(EntryName, on ? CurrentCommand() : null);

    /// At start-up: an entry that points at an old location (the program was moved) is rewritten to this one.
    public static void RepairIfMoved()
    {
        if (StartupEntry.Command(EntryName) is { } stored && CurrentCommand() is { } current && stored != current)
            StartupEntry.Set(EntryName, current, clearDisabled: false);
    }

    private static string? CurrentCommand() =>
        Environment.ProcessPath is { } path ? $"\"{path}\" {BackgroundArgument}" : null;
}
