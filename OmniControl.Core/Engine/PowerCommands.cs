using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>What Windows' power plan overlay can be set to ("Power mode" in Settings).</summary>
public enum PowerMode { BestEfficiency, Balanced, BestPerformance }

/// <summary>
/// Puts the PC to sleep, hibernates, shuts down, restarts, locks, signs out and sets the power mode. Everything runs as the normal user
/// (no elevation), and shut down, restart and sign out force programs closed only when asked to, so by default a program with unsaved work can still say no.
/// Each call returns false when Windows refused. The caller decides when it is wise to call these (the app asks first).
/// </summary>
public static class PowerCommands
{
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, int length, IntPtr previous, IntPtr returnLength);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    // TOKEN_PRIVILEGES with one entry: count, LUID, attributes.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8;
    private const int PrivilegeEnabled = 2;

    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(in Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    // Windows' fixed identifiers of the three power mode overlays (balanced is the empty one).
    private static readonly Guid EfficiencyOverlay = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid PerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

    // With hibernation turned on, Windows may hibernate even when asked to sleep; that is how SetSuspendState behaves.
    // Both return only after the PC wakes again, so call them off the UI thread.
    public static bool Sleep() => Suspend(hibernate: false);

    public static bool Hibernate() => Suspend(hibernate: true);

    public static bool Lock() => LockWorkStation();

    // WM_SYSCOMMAND (0x0112) with SC_MONITORPOWER (0xF170) = 2 to every top-level window switches all screens off; any input wakes them again.
    public static bool TurnOffDisplays() => PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));

    // SC_MONITORPOWER = -1 switches them on again (the same as the input that wakes them).
    public static bool TurnOnDisplays() => PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(-1));

    public static bool ShutDown(bool force = false) => RunShutdown("/s /t 0", force);

    public static bool Restart(bool force = false) => RunShutdown("/r /t 0", force);

    public static bool SignOut(bool force = false) => RunShutdown("/l", force);

    public static bool SetMode(PowerMode mode)
    {
        var overlay = mode switch { PowerMode.BestEfficiency => EfficiencyOverlay, PowerMode.BestPerformance => PerformanceOverlay, _ => Guid.Empty };
        return PowerSetActiveOverlayScheme(overlay) == 0;
    }

    /// <summary>The power mode in force now, or null when Windows can't say (an older Windows, or a mode set by the maker).</summary>
    public static PowerMode? CurrentMode()
    {
        Guid overlay;
        try
        {
            if (PowerGetEffectiveOverlayScheme(out overlay) != 0) return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null; // a Windows before power modes: there is no mode to read
        }
        if (overlay == EfficiencyOverlay) return PowerMode.BestEfficiency;
        if (overlay == PerformanceOverlay) return PowerMode.BestPerformance;
        return overlay == Guid.Empty ? PowerMode.Balanced : null;
    }

    // shutdown.exe is Windows' own tool for this and works for the signed-in user without elevation; /f (only when asked) closes programs that object.
    private static bool RunShutdown(string arguments, bool force)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("shutdown.exe", force ? arguments + " /f" : arguments) { UseShellExecute = false, CreateNoWindow = true });
            if (process is null) return false;
            // A refusal (a shut down already under way, a policy) is a non-zero exit code; still running after the wait counts as accepted.
            return !process.WaitForExit(CoreTiming.ShutdownExeWaitMs) || process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    // SetSuspendState needs the shut-down privilege switched on in the process's own token (every signed-in user holds it, switched off).
    // If switching it on fails, Windows is still asked: it then says no and the caller reports that.
    private static bool Suspend(bool hibernate)
    {
        if (OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            try
            {
                if (LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
                {
                    var state = new TokenPrivilege { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
                    AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        return SetSuspendState(hibernate, false, false);
    }
}
