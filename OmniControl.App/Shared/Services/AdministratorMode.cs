namespace OmniControl.App.Shared.Services;

/// Running as administrator. With "Run as administrator" on (Mouse and Trackpad: Windows passes no mouse input to administrator programs
/// otherwise; network limits need it too), a start that is not elevated starts OmniControl again through its sign-in task
/// (<see cref="SignInTask"/>, no prompt) or, without the task, with Windows' administrator prompt, and ends itself; when the user refuses, it
/// carries on as it is. An elevated copy keeps the task in step with the setting. A restart as administrator starts the same way.
public static class AdministratorMode
{
    private const string SetupArgument = "--sign-in-task", SetupOn = "on";

    /// Whether this copy runs as administrator (it cannot change while it runs).
    public static bool IsElevated { get; } = Elevation.IsElevated();

    private static string Self => Environment.ProcessPath ?? string.Empty;

    /// Setup's <c>--sign-in-task on|off</c> (run elevated): adds or removes the task for this copy; the exit code (0 done, 1 not), or null
    /// when the arguments ask something else. A failed add is tried again by the app's first elevated start.
    public static int? FromSetup(string[] args)
    {
        var at = Array.IndexOf(args, SetupArgument);
        if (at < 0) return null;
        var on = at + 1 < args.Length && args[at + 1] == SetupOn;
        return (on ? SignInTask.Set(Self) : SignInTask.Remove()) is null ? 0 : 1;
    }

    /// At the very start, before anything of the app runs: true when an elevated copy was started and this one must end. A copy the task
    /// started never starts another one.
    public static bool RelaunchIfWanted(string[] args, Func<bool> wanted) =>
        !IsElevated && !args.Contains(SignInTask.FromTask) && wanted() && StartElevated(args) == HelperFailure.None;

    /// Starts the next run as administrator and quits this one (UI thread). Returns a plain sentence when it could not (the user refused
    /// the prompt, Windows blocked it), null when it worked or nothing was needed.
    public static async Task<string?> RestartElevatedAsync()
    {
        if (IsElevated) return null;
        switch (await Task.Run(() => StartElevated(AppRestart.NextArguments())))
        {
            case HelperFailure.None:
                AppLifecycle.RequestQuit();
                return null;
            case HelperFailure.Cancelled:
                return "Windows didn't get your OK, so OmniControl keeps running as before.";
            default:
                return "Windows didn't start OmniControl as administrator, so it keeps running as before.";
        }
    }

    /// Makes the sign-in task follow the setting, on a worker; only an elevated copy may change it (one that is not leaves it as it is).
    public static void FollowSetting(bool on, Action<string> report)
    {
        if (!IsElevated) return;
        var self = Self;
        Task.Run(() => Guard.Run("Sign-in task", () =>
        {
            if ((on ? SignInTask.IsFor(self) ? null : SignInTask.Set(self) : SignInTask.Remove()) is { } problem) report(problem);
        }));
    }

    private static HelperFailure StartElevated(IReadOnlyList<string> args) =>
        SignInTask.Run(Self, args) ? HelperFailure.None : Elevation.StartElevated(Self, args);
}
