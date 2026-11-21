namespace OmniControl.App.Shared.Services;

/// Running as administrator. With "Run as administrator" on (Mouse and Trackpad: Windows passes no mouse input to administrator programs
/// otherwise; network limits use the network service instead, <see cref="NetworkServiceMode"/>), a start that is not elevated starts OmniControl again through its sign-in task
/// (<see cref="SignInTask"/>, no prompt) or, without the task, with Windows' administrator prompt, and ends itself; when the user refuses, it
/// carries on as it is. Only Setup adds the task; an elevated copy removes it when the setting is off or the task is not OmniControl's own.
/// A restart as administrator starts the same way.
public static class AdministratorMode
{
    // Retyped in tools/setup.iss.
    private const string SetupArgument = "--sign-in-task", SetupOn = "on", SetupKeep = "keep";

    /// Whether this copy runs as administrator (it cannot change while it runs).
    public static bool IsElevated { get; } = Elevation.IsElevated();

    private static string Self => Environment.ProcessPath ?? string.Empty;

    /// Setup's <c>--sign-in-task on|keep|off</c> (run elevated): adds the task for this copy and the user signed in here, keeps one from
    /// before only when it is exactly that, or removes it; the exit code (0 done, 1 when it logged what failed or what it removed), or null
    /// when the arguments ask something else.
    public static int? FromSetup(string[] args)
    {
        var at = Array.IndexOf(args, SetupArgument);
        if (at < 0) return null;
        var problem = args.ElementAtOrDefault(at + 1) switch
        {
            SetupOn => SignInTask.Set(Self),
            SetupKeep => SignInTask.RemoveInvalid(Self) is ({ } wrong, var failed) ? $"removed ({wrong}){(failed is null ? null : $", but Windows didn't remove it ({failed})")}" : null,
            _ => SignInTask.Remove(),
        };
        if (problem is null) return 0;
        AppDataStore.AppendLog(Guard.LogFile, $"Sign-in task: {problem}");
        AppDataStore.FlushLogs();
        return 1;
    }

    /// The arguments this copy may act on. Anything running as the user may run the sign-in task with arguments of its choosing, so a copy
    /// it started (elevated, <see cref="SignInTask.FromTask"/>) keeps only a restart's <c>--after &lt;id&gt;</c> and <c>--background</c>.
    public static string[] Trusted(string[] args)
    {
        if (Array.IndexOf(args, SignInTask.FromTask) < 0) return args;
        var kept = new List<string> { SignInTask.FromTask };
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == StartupRegistration.BackgroundArgument) kept.Add(args[i]);
            else if (args[i] == AppRestart.AfterArgument && i + 1 < args.Length && int.TryParse(args[i + 1], out _)) kept.AddRange(new[] { args[i], args[++i] });
        }
        return kept.ToArray();
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
    /// Off removes it; on keeps it only while it is OmniControl's own (never adds one: only Setup does, with the person's tick).
    public static void FollowSetting(bool on, Action<string> report)
    {
        if (!IsElevated) return;
        Task.Run(() => Guard.Run("Sign-in task", () =>
        {
            if (!on)
            {
                if (SignInTask.Remove() is { } problem) report(problem);
                return;
            }
            if (SignInTask.RemoveInvalid(null) is not ({ } wrong, var failed)) return;
            AppDataStore.AppendLog(Guard.LogFile, failed is null ? $"Sign-in task removed: {wrong}" : $"Sign-in task not removed ({failed}): {wrong}");
            report(Guard.SeeLog(failed is null ? SignInTask.Removed : "OmniControl's sign-in task isn't the one Setup makes, and Windows didn't remove it."));
        }));
    }

    private static HelperFailure StartElevated(IReadOnlyList<string> args) =>
        SignInTask.Run(Self, args) ? HelperFailure.None : Elevation.StartElevated(Self, args);
}
