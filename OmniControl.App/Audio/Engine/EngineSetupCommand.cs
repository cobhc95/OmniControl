using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// OmniControl's hidden command-line mode for installing or removing OmniControl APO. <see cref="EngineInstaller"/> starts the
/// running OmniControl.exe again with the administrator prompt and these arguments, so no second program has to be trusted by
/// Windows (a separate helper was blocked by Smart App Control). It runs before any window opens and exits:
///   OmniControl.exe --engine-setup preview-install --device &lt;endpoint id&gt; --source &lt;folder&gt; [--log &lt;file&gt;]   (changes nothing)
///   OmniControl.exe --engine-setup install         --device ... --source ... --log &lt;file&gt;               (administrator; undoes itself on failure)
///   OmniControl.exe --engine-setup preview-uninstall [--log &lt;file&gt;]                                         (changes nothing)
///   OmniControl.exe --engine-setup uninstall --log &lt;file&gt;                                                  (administrator)
/// Everything it says goes to the log (and to a console when there is one).
/// </summary>
public static class EngineSetupCommand
{
    /// <summary>The first argument that selects this mode.</summary>
    public const string Flag = "--engine-setup";

    /// <summary>Exit codes, read back by <see cref="EngineInstaller"/>.</summary>
    public const int Done = 0, PlanProblems = 1, StepFailed = 2, NotAdministrator = 3, BadArguments = 4;

    /// <summary>The line every run writes to the log first, so the app can tell that this mode really started.</summary>
    public const string StartLine = "OmniControl engine setup started:";

    /// <summary>Runs the setup mode when <paramref name="args"/> ask for it; null means start the app as usual.</summary>
    public static int? TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != Flag) return null;
        if (args.Length < 2) return Usage();
        var options = Options(args.Skip(2));
        using var log = options.TryGetValue("log", out var logPath) ? OpenLog(logPath) : null;
        void Say(string line)
        {
            Console.WriteLine(line);
            log?.WriteLine(line);
        }
        // A failure nobody expected still ends in the log (with the stack), not only in an invisible console.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Say($"Unexpected error: {e.ExceptionObject}");

        Say($"{StartLine} {args[1]} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        switch (args[1])
        {
            case "preview-install" or "install":
                if (!options.TryGetValue("device", out var device) || !options.TryGetValue("source", out var source)) return Usage();
                var install = ApoPlanner.PlanInstall(source, device);
                return args[1] == "install" ? Apply(install, Say, rollback: true) : Preview(install, Say);
            case "preview-uninstall" or "uninstall":
                var uninstall = ApoPlanner.PlanUninstall();
                return args[1] == "uninstall" ? Apply(uninstall, Say, rollback: false) : Preview(uninstall, Say);
            default:
                return Usage();
        }
    }

    /// <summary>The arguments for one verb (what <see cref="EngineInstaller"/> passes to the elevated copy of OmniControl).</summary>
    public static string Arguments(string verb, string log, string? device = null, string? source = null) =>
        device is null || source is null
            ? $"{Flag} {verb} --log \"{log}\""
            : $"{Flag} {verb} --device \"{device}\" --source \"{source}\" --log \"{log}\"";

    private static StreamWriter? OpenLog(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // the setup still runs; the app then reports that the log says nothing
        }
    }

    private static int Preview(ApoPlan plan, Action<string> say)
    {
        say("PREVIEW ONLY: nothing is changed.");
        say(plan.Describe());
        return plan.Problems.Count == 0 ? Done : PlanProblems;
    }

    private static int Apply(ApoPlan plan, Action<string> say, bool rollback)
    {
        say(plan.Describe());
        if (!plan.CanApply) return PlanProblems;
        if (!Elevation.IsElevated())
        {
            say("Run as administrator to make these changes.");
            return NotAdministrator;
        }
        if (ApoApplier.Apply(plan, say) is not { } error) return Done;
        say(error);
        if (rollback)
        {
            say("Undoing the install: putting the saved values back.");
            var undo = ApoPlanner.PlanUninstall();
            if (undo.CanApply) ApoApplier.Apply(undo, say);
        }
        return StepFailed;
    }

    private static Dictionary<string, string> Options(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal)) name = arg[2..];
            else if (name is not null) { result[name] = arg; name = null; }
        }
        return result;
    }

    private static int Usage()
    {
        Console.WriteLine($"OmniControl {Flag} preview-install|install --device <endpoint id> --source <folder> [--log <file>]");
        Console.WriteLine($"OmniControl {Flag} preview-uninstall|uninstall [--log <file>]");
        return BadArguments;
    }
}
