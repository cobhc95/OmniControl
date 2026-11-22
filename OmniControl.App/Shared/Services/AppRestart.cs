using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Shared.Services;

/// Restarting OmniControl: the running copy starts the next one with <see cref="AfterArgument"/> and its process id, then exits; the
/// next one waits for it to end (it saves its settings as it closes), then carries out a staged import or wipe
/// (<see cref="PendingRestore"/>) before anything is loaded. Every start carries out a staged one, so nothing is lost if the restart fails.
public static class AppRestart
{
    public const string AfterArgument = "--after";
    private const string LogName = LogFiles.Backup;

    /// Starts the next run of OmniControl; false when Windows refused (the user then starts it again by hand).
    public static bool StartNext()
    {
        if (Environment.ProcessPath is not { } self) return false;
        try
        {
            var start = new ProcessStartInfo(self) { UseShellExecute = false };
            foreach (var argument in NextArguments()) start.ArgumentList.Add(argument);
            using var next = Process.Start(start);
            return next is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppDataStore.AppendLog(LogName, $"restart failed: {ex.Message}");
            return false;
        }
    }

    /// The arguments of the next run: it waits for this one to end.
    public static string[] NextArguments() => new[] { AfterArgument, Environment.ProcessId.ToString() };

    /// At start, before anything is loaded: waits for the run that restarted this one, then carries out a staged import or wipe.
    public static void BeforeStart(string[] args)
    {
        WaitForPrevious(args);
        if (PendingRestore.ApplyPending(AppDataStore.Folder) is { } done) AppDataStore.AppendLog(LogName, done);
        StoreMoves.RunAtStart();
    }

    /// True when this run was started by a restart of the run before it (which may still be closing).
    public static bool IsRestart(string[] args) => Array.IndexOf(args, AfterArgument) >= 0;

    private static void WaitForPrevious(string[] args)
    {
        var at = Array.IndexOf(args, AfterArgument);
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var id)) return;
        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(Timing.RestartWait);
        }
        catch (ArgumentException)
        {
            // It has already ended.
        }
        catch (InvalidOperationException)
        {
            // It ended while being looked at.
        }
    }
}
