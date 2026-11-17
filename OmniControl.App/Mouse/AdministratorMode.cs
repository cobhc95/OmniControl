using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Mouse;

/// <summary>
/// "Run as administrator" (Where mouse features work): Windows does not pass mouse input to programs run as administrator unless
/// OmniControl runs as administrator too. With the setting on, every start that is not already elevated starts OmniControl again with the
/// administrator prompt and ends itself; when the user refuses, it carries on as it is.
/// </summary>
public static class AdministratorMode
{
    /// <summary>At the very start, before anything of the app runs: true when an elevated copy was started and this one must end.</summary>
    public static bool RelaunchIfWanted(string[] args) =>
        !Elevation.IsElevated() && MouseSettings.Load().RunAsAdministrator
        && Elevation.StartElevated(Environment.ProcessPath ?? string.Empty, args) == HelperFailure.None;

    /// <summary>
    /// The switch was turned on while running: starts the elevated copy and quits this one. Returns a plain sentence when it could not
    /// (the user refused the prompt, Windows blocked it), null when it worked or nothing was needed.
    /// </summary>
    public static string? RestartElevated()
    {
        if (Elevation.IsElevated()) return null;
        switch (AppRestart.StartNextElevated())
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
}
