using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.App.Rules;

/// <summary>
/// Runs a rule's PowerShell or CMD command: never elevated, hidden unless asked otherwise, stopped (with everything it started) after
/// its timeout. Output is discarded; the error text of a failure is kept (shortened) for the rules log.
/// </summary>
public static class ScriptRunner
{
    public const string PowerShell = "PowerShell", Cmd = "CMD";

    // Enough of the error text to recognise the problem in the log; a command that prints megabytes cannot fill memory.
    private const int ErrorTextLimit = 2000;

    /// <summary>The program and arguments a command runs as: what the confirm window shows and what is started.</summary>
    public static (string File, string[] Arguments) CommandLine(string shell, string command) => shell == Cmd
        ? ("cmd.exe", new[] { "/d", "/c", command })
        : ("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", command });

    /// <summary>The command line as one readable line.</summary>
    public static string Describe(string shell, string command)
    {
        var (file, arguments) = CommandLine(shell, command);
        return $"{file} {string.Join(' ', arguments)}";
    }

    /// <summary>Runs the command and waits for it; null when it ended well, otherwise one line saying what went wrong.</summary>
    public static async Task<string?> RunAsync(string shell, string command, bool hidden, TimeSpan timeout)
    {
        var (file, arguments) = CommandLine(shell, command);
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
            RedirectStandardError = hidden, // a visible window shows its own errors
            RedirectStandardOutput = hidden,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start it.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"couldn't start {file}: {ex.Message}";
        }

        using (process)
        {
            var errors = hidden ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);
            var output = hidden ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty); // read so a full pipe never blocks it
            using var limit = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                StopTree(process);
                await Drain(errors, output);
                return $"stopped after {timeout.TotalSeconds:0} s (its timeout)";
            }
            await Drain(errors, output);
            if (process.ExitCode == 0) return null;
            var text = errors.IsCompletedSuccessfully ? errors.Result.Trim() : string.Empty;
            if (text.Length > ErrorTextLimit) text = text[..ErrorTextLimit];
            return text.Length == 0 ? $"ended with code {process.ExitCode}" : $"ended with code {process.ExitCode}: {text}";
        }
    }

    /// <summary>Lets the output readers finish; a pipe broken by stopping the command only means there is no more to read.</summary>
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
