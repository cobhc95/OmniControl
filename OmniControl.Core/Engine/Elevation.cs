using System.Security.Principal;

namespace OmniControl.Core.Engine;

/// Whether the program runs as administrator, and starting it again as administrator (Windows asks the user once).
public static class Elevation
{
    /// Windows' "the user said No to the administrator prompt" error.
    private const int Cancelled = 1223;

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// Starts <paramref name="exe"/> as administrator without waiting for it. None: it started; Cancelled: the user refused the prompt.
    public static HelperFailure StartElevated(string exe, IEnumerable<string> arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, Quoted(arguments)) { UseShellExecute = true, Verb = "runas" });
            return process is null ? HelperFailure.NotStarted : HelperFailure.None;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return HelperFailure.Cancelled;
        }
        catch (Win32Exception ex) when (AppControl.IsBlock(ex.NativeErrorCode))
        {
            return HelperFailure.Blocked;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return HelperFailure.NotStarted;
        }
    }

    /// The arguments as one command line, each in quotes.
    internal static string Quoted(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(a => $"\"{a}\""));
}
