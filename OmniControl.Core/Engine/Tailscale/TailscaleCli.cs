namespace OmniControl.Core.Engine.Tailscale;

/// Finds Tailscale's command-line program and runs it. Only <see cref="Run"/> is a read; Start and SignIn are the fixes a person presses.
public static class TailscaleCli
{
    private const string CliName = "tailscale.exe", TrayName = "tailscale-ipn.exe", Folder = "Tailscale";

    /// The path of tailscale.exe (Program Files, then the PATH), or null when Tailscale is not installed.
    public static string? Find()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        foreach (var root in roots.Where(r => r.Length > 0))
        {
            var path = Path.Combine(root, Folder, CliName);
            if (File.Exists(path)) return path;
        }
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return dirs.Select(d => Path.Combine(d.Trim('"'), CliName)).FirstOrDefault(File.Exists);
    }

    /// Runs the program hidden, with the user's normal rights (it may come from a folder on the PATH the user can change), and returns what
    /// it printed; null when it failed, did not start or took too long.
    public static string? Run(string cli, params string[] arguments)
    {
        try
        {
            using var started = NormalRights.Start(cli, arguments, hidden: true, readOutput: true);
            var process = started.Process;
            var output = started.Output!.ReadToEndAsync();
            var errors = started.Errors!.ReadToEndAsync();
            if (!process.WaitForExit(CoreTiming.TailscaleCliWaitMs))
            {
                process.Kill(true);
                return null;
            }
            return process.ExitCode == 0 && output.Wait(CoreTiming.TailscaleCliWaitMs) && errors.Wait(CoreTiming.TailscaleCliWaitMs) ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
        {
            return null;
        }
    }

    /// "Start Tailscale": its tray program when Tailscale is not answering, otherwise "up" for one that is switched off. False when nothing could be started.
    public static bool Start(bool answering)
    {
        if (Find() is not { } cli) return false;
        var tray = Path.Combine(Path.GetDirectoryName(cli) ?? string.Empty, TrayName);
        return answering ? Launch(cli, "up") : File.Exists(tray) ? Launch(tray) : Launch(cli, "up");
    }

    /// "Sign in": Tailscale's own login, which opens its page in the browser.
    public static bool SignIn() => Find() is { } cli && Launch(cli, "login");

    private static bool Launch(string exe, params string[] arguments)
    {
        try
        {
            using var started = NormalRights.Start(exe, arguments, hidden: true, readOutput: false);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
