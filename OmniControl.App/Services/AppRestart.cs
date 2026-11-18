using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Services.Backup;
using OmniControl.Core.Engine;

namespace OmniControl.App.Services;

/// <summary>
/// Restarting OmniControl: the running copy starts the next one with <see cref="AfterArgument"/> and its process id, then exits; the
/// next one waits for it to end (it saves its settings as it closes), then carries out a staged import or wipe
/// (<see cref="PendingRestore"/>) before anything is loaded. Every start carries out a staged one, so nothing is lost if the restart fails.
/// </summary>
public static class AppRestart
{
    private const string AfterArgument = "--after";
    private const string LogName = "backup-log.txt";

    /// <summary>Starts the next run of OmniControl; false when Windows refused (the user then starts it again by hand).</summary>
    public static bool StartNext()
    {
        if (Environment.ProcessPath is not { } self) return false;
        try
        {
            var start = new ProcessStartInfo(self) { UseShellExecute = false };
            start.ArgumentList.Add(AfterArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var next = Process.Start(start);
            return next is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppDataStore.AppendLog(LogName, $"restart failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Starts the next run as administrator (Windows asks the user); the caller exits when this says None, and stays as it is when the user refused.</summary>
    public static HelperFailure StartNextElevated() =>
        Environment.ProcessPath is { } self
            ? Elevation.StartElevated(self, new[] { AfterArgument, Environment.ProcessId.ToString() })
            : HelperFailure.NotStarted;

    /// <summary>At start, before anything is loaded: waits for the run that restarted this one, then carries out a staged import or wipe.</summary>
    public static void BeforeStart(string[] args)
    {
        WaitForPrevious(args);
        if (PendingRestore.ApplyPending(AppDataStore.Folder) is { } done) AppDataStore.AppendLog(LogName, done);
    }

    /// <summary>True when this run was started by a restart of the run before it (which may still be closing).</summary>
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
