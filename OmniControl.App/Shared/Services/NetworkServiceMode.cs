using System.Security.Principal;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.App.Shared.Services;

/// THE way the network service is run, installed and removed, all through OmniControl.exe's own command line: Windows starts the service
/// as <c>--network-service run &lt;user SID&gt;</c>; <c>install &lt;account&gt;</c> and <c>remove</c> run elevated, started by Setup (in its one
/// admin step) or by <see cref="InstallAsync"/> / <see cref="RemoveAsync"/> behind one Windows prompt.
public static class NetworkServiceMode
{
    // Retyped in tools/setup.iss.
    private const string Argument = "--network-service", Run = "run", Install = "install", Remove = "remove";

    private static string Self => Environment.ProcessPath ?? string.Empty;

    /// At the very start: the exit code when the arguments ask for the network service (0 done, 1 not), else null.
    public static int? FromArguments(string[] args)
    {
        var at = Array.IndexOf(args, Argument);
        if (at < 0) return null;
        var (verb, value) = (args.ElementAtOrDefault(at + 1), args.ElementAtOrDefault(at + 2) ?? string.Empty);
        return verb switch
        {
            Run => NetworkServiceHost.Run(value, Timing.ResourcesShaping, Timing.NetworkServiceIdle, Timing.NetworkServiceSpeedsIdle),
            Install => Done("install", InstallFor(value)),
            Remove => Done("remove", NetworkServiceSetup.Remove(Timing.NetworkServiceStopWait)),
            _ => 1,
        };
    }

    /// The elevated install: the service runs this copy of OmniControl for the account's SID (named, so an administrator's prompt for a
    /// standard user still serves that user).
    private static string? InstallFor(string account)
    {
        SecurityIdentifier user;
        try { user = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier)); }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException || ex.GetType() == typeof(SystemException)) // Windows' lookup failed
        {
            return $"Windows doesn't know the account “{account}”.";
        }
        return user.IsAccountSid() ? NetworkServiceSetup.Install(Self, $"{Argument} {Run} {user.Value}", user) : $"“{account}” is not a user account.";
    }

    /// True when this copy may host the service: installed where only administrators can change its files (not a portable copy). Reads the
    /// folders' access lists, so off the UI thread.
    public static bool CanInstall() => NetworkServiceSetup.CanServeFrom(Self, wholeFolder: true);

    private static int Done(string what, string? problem)
    {
        if (problem is null) return 0;
        AppDataStore.AppendLog(Guard.LogFile, $"Network service {what}: {problem}");
        AppDataStore.FlushLogs();
        return 1;
    }

    /// Installs the service for the user running OmniControl (Windows asks once) and waits. Null when done, else one plain sentence.
    public static Task<string?> InstallAsync() => Task.Run(() =>
    {
        using var me = WindowsIdentity.GetCurrent();
        return Says(HelperProcess.RunElevated(Self, $"{Argument} {Install} \"{me.Name}\"", hidden: true), "installed");
    });

    /// Stops and removes the service (Windows asks once); its rules leave Windows as it stops. Null when done, else one plain sentence.
    public static Task<string?> RemoveAsync() => Task.Run(() => NetworkServiceSetup.Installed
        ? Says(HelperProcess.RunElevated(Self, $"{Argument} {Remove}", hidden: true), "removed")
        : "The network service is not installed.");

    private static string? Says(HelperResult result, string done) => result.Failure switch
    {
        HelperFailure.None when result.ExitCode == 0 => null,
        HelperFailure.Cancelled => $"Windows didn't get your OK, so the network service was not {done}.",
        HelperFailure.None => $"The network service was not {done}; the details are in {Guard.LogPath}.",
        _ => $"Windows didn't start OmniControl's set-up, so the network service was not {done}.",
    };
}
