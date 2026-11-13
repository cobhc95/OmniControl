using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>Why a helper program did not finish normally.</summary>
public enum HelperFailure
{
    /// <summary>It ran and ended; look at the exit code.</summary>
    None,
    /// <summary>The user said No to the administrator prompt.</summary>
    Cancelled,
    /// <summary>Windows App Control (Smart App Control, WDAC, AppLocker) refused to start the file.</summary>
    Blocked,
    /// <summary>It could not be started for another reason (missing file, ...).</summary>
    NotStarted,
    /// <summary>It is still running after the wait (an installer the user is still clicking through).</summary>
    StillRunning,
}

/// <summary>How a helper program ended: its exit code, or why it did not end normally, and the technical detail for the log.</summary>
public sealed record HelperResult(int ExitCode, HelperFailure Failure, string Detail);

/// <summary>Starts a helper program (OmniControl's own elevated setup mode, an installer) and waits for it. Call off the UI thread.</summary>
public static class HelperProcess
{
    /// <summary>Windows' "the user said No to the administrator prompt" error.</summary>
    private const int Cancelled = 1223;

    /// <summary>
    /// Runs a program with the administrator prompt (UAC) and waits for it; the user may refuse. It starts in its own folder:
    /// Equalizer APO's Qt programs find their "qt" plugin folder relative to the current folder and otherwise fail with
    /// "no Qt platform plugin could be initialized".
    /// </summary>
    public static HelperResult RunElevated(string exe, string arguments)
    {
        var name = Path.GetFileName(exe);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? string.Empty,
            });
            if (process is null) return new HelperResult(-1, HelperFailure.NotStarted, $"{name} did not start.");
            if (!process.WaitForExit(CoreTiming.ElevatedHelperTimeoutMs))
                return new HelperResult(-1, HelperFailure.StillRunning, $"{name} was still running after {CoreTiming.ElevatedHelperTimeoutMs / 1000} s.");
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
