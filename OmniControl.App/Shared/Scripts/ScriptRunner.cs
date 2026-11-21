using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.App.Shared.Scripts;

/// Runs a rule's PowerShell or CMD command, a custom action's script or a saved script: never elevated (<see cref="NormalRights"/>), hidden unless asked otherwise, stopped (with everything it started) after
/// its timeout. Output is discarded; the error text of a failure is kept (shortened) for the rules log.
public static class ScriptRunner
{
    public const string PowerShell = "PowerShell", Cmd = "CMD";

    // Enough of the error text to recognise the problem in the log; a command that prints megabytes cannot fill memory.
    private const int ErrorTextLimit = 2000;

    /// The program and arguments a command runs as: what the confirm window shows and what is started.
    public static (string File, string[] Arguments) CommandLine(string shell, string command) => shell == Cmd
        ? (SystemTools.Cmd, new[] { "/d", "/c", command })
        : (SystemTools.PowerShell, new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command });

    /// The command line as one readable line.
    public static string Describe(string shell, string command)
    {
        var (file, arguments) = CommandLine(shell, command);
        return $"{Path.GetFileName(file)} {string.Join(' ', arguments)}";
    }

    /// Runs the command and waits for it; null when it ended well, otherwise one line saying what went wrong.
    public static Task<string?> RunAsync(string shell, string command, bool hidden, TimeSpan timeout)
    {
        var (file, arguments) = CommandLine(shell, command);
        return RunAsync(file, arguments, hidden, timeout);
    }

    /// Runs <paramref name="file"/> with <paramref name="arguments"/> (each passed as is) and waits for it, the same way as a command.
    /// <paramref name="environment"/> adds variables for this one run (a secret that must not be in the arguments); <paramref name="output"/>,
    /// when given, receives the text the program printed (shortened) once it ended. <paramref name="stop"/> ends the wait early and kills the program
    /// and everything it started.
    public static async Task<string?> RunAsync(string file, IReadOnlyList<string> arguments, bool hidden, TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null, Action<string>? output = null, CancellationToken stop = default)
    {
        NormalRights.Started started;
        try
        {
            started = NormalRights.Start(file, arguments, hidden, readOutput: hidden, environment); // a visible window shows its own errors
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"couldn't start {file}: {ex.Message}";
        }

        using (started)
        {
            var process = started.Process;
            var errors = started.Errors?.ReadToEndAsync() ?? Task.FromResult(string.Empty);
            var printed = started.Output?.ReadToEndAsync() ?? Task.FromResult(string.Empty); // read so a full pipe never blocks it
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                StopTree(process);
                await Drain(errors, printed);
                if (stop.IsCancellationRequested) return "stopped by the user";
                return $"stopped after {timeout.TotalSeconds:0} s (its timeout)";
            }
            await Drain(errors, printed);
            if (output is not null && printed.IsCompletedSuccessfully) output(Shortened(printed.Result.Trim()));
            if (process.ExitCode == 0) return null;
            var text = Shortened(errors.IsCompletedSuccessfully ? errors.Result.Trim() : string.Empty);
            return text.Length == 0 ? $"ended with code {process.ExitCode}" : $"ended with code {process.ExitCode}: {text}";
        }
    }

    private static string Shortened(string text) => text.Length > ErrorTextLimit ? text[..ErrorTextLimit] : text;

    /// Lets the output readers finish; a pipe broken by stopping the command only means there is no more to read.
    private static async Task Drain(params Task<string>[] readers)
    {
        try
        {
            await Task.WhenAll(readers);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The output is discarded anyway; only the exit code and the error text that did arrive matter.
        }
    }

    private static void StopTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // It ended on its own just now, or Windows refused: nothing more can be done from here.
        }
    }
}
