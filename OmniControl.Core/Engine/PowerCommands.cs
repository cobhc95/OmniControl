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
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(in Guid overlay);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    // Windows' fixed identifiers of the three power mode overlays (balanced is the empty one).
    private static readonly Guid EfficiencyOverlay = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid PerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

    // With hibernation turned on, Windows may hibernate even when asked to sleep; that is how SetSuspendState behaves.
    public static bool Sleep() => SetSuspendState(false, false, false);

    public static bool Hibernate() => SetSuspendState(true, false, false);

    public static bool Lock() => LockWorkStation();

    // WM_SYSCOMMAND (0x0112) with SC_MONITORPOWER (0xF170) = 2 to every top-level window switches all screens off; any input wakes them again.
    public static bool TurnOffDisplays() => PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));

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
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
