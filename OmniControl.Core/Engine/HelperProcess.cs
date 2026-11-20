namespace OmniControl.Core.Engine;

/// Why a helper program did not finish normally.
public enum HelperFailure
{
    /// It ran and ended; look at the exit code.
    None,
    /// The user said No to the administrator prompt.
    Cancelled,
    /// Windows App Control (Smart App Control, WDAC, AppLocker) refused to start the file.
    Blocked,
    /// It could not be started for another reason (missing file, ...).
    NotStarted,
    /// It is still running after the wait (an installer the user is still clicking through).
    StillRunning,
}

/// How a helper program ended: its exit code, or why it did not end normally, and the technical detail for the log.
public sealed record HelperResult(int ExitCode, HelperFailure Failure, string Detail);

/// Starts a helper program (Equalizer APO's installer and Device Selector, Windows' netcfg) elevated and waits for it. Call off the UI thread.
public static class HelperProcess
{
    /// Windows' "the user said No to the administrator prompt" error.
    private const int Cancelled = 1223;

    /// Runs a program as administrator and waits for it: Windows asks (UAC) unless OmniControl already runs as administrator; the user may
    /// refuse. It starts in its own folder: Equalizer APO's Qt programs find their "qt" plugin folder relative to the current folder and
    /// otherwise fail with "no Qt platform plugin could be initialized".
    /// <param name="waitMs">How long to wait for it to end (<see cref="HelperFailure.StillRunning"/> after that).</param>
    /// <param name="hidden">A console tool: no window is shown.</param>
    public static HelperResult RunElevated(string exe, string arguments, int waitMs = CoreTiming.ElevatedHelperTimeoutMs, bool hidden = false)
    {
        var name = Path.GetFileName(exe);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? string.Empty,
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            });
            if (process is null) return new HelperResult(-1, HelperFailure.NotStarted, $"{name} did not start.");
            if (!process.WaitForExit(waitMs))
                return new HelperResult(-1, HelperFailure.StillRunning, $"{name} was still running after {waitMs / 1000} s.");
            return new HelperResult(process.ExitCode, HelperFailure.None, $"{name} ended with code {process.ExitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return new HelperResult(-1, HelperFailure.Cancelled, $"{name}: the administrator prompt was refused.");
        }
        catch (Win32Exception ex) when (AppControl.IsBlock(ex.NativeErrorCode))
        {
            return new HelperResult(-1, HelperFailure.Blocked, $"{name}: {ex.Message} (Windows error {ex.NativeErrorCode}).");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new HelperResult(-1, HelperFailure.NotStarted, $"{name}: {ex.Message}");
        }
    }
}
