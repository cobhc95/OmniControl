using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace OmniControl.Core.Engine;

/// <summary>Whether the program runs as administrator, and starting it again as administrator (Windows asks the user once).</summary>
public static class Elevation
{
    /// <summary>Windows' "the user said No to the administrator prompt" error.</summary>
    private const int Cancelled = 1223;

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Starts <paramref name="exe"/> as administrator without waiting for it. None: it started; Cancelled: the user refused the prompt.</summary>
    public static HelperFailure StartElevated(string exe, IEnumerable<string> arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, string.Join(' ', arguments.Select(a => $"\"{a}\""))) { UseShellExecute = true, Verb = "runas" });
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
}
