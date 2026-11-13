using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// Lists the displays attached to the desktop and names them. Windows answers from what it already knows, without talking to any
/// monitor, so this is cheap enough to repeat whenever something might have changed.
/// </summary>
internal static class DisplayEnumerator
{
    private const string BuiltInName = "Built-in display";

    /// <summary>Every display with its handle (needed to reach the monitor's DDC/CI), in Windows' order.</summary>
    public static List<(IntPtr Handle, DisplayInfo Info)> Enumerate()
    {
        var handles = new List<IntPtr>();
        var proc = new Dxva2Native.MonitorEnumProc((hMonitor, _, _, _) => { handles.Add(hMonitor); return true; });
        Dxva2Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);

        var targets = DisplayConfigNative.ReadTargets();
        var found = new List<(IntPtr, DisplayInfo)>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in handles)
        {
            var info = new Dxva2Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Dxva2Native.MONITORINFOEX>() };
            if (!Dxva2Native.GetMonitorInfo(handle, ref info)) continue;
            var device = info.szDevice ?? string.Empty;
            targets.TryGetValue(device, out var target);

            var name = target.IsInternal ? BuiltInName
                : !string.IsNullOrWhiteSpace(target.FriendlyName) ? target.FriendlyName.Trim()
                : $"Display {found.Count + 1}";
            // Two monitors of one model would otherwise be told apart by nothing.
            used[name] = used.GetValueOrDefault(name) + 1;
            if (used[name] > 1) name = $"{name} ({used[name]})";

            found.Add((handle, new DisplayInfo(device, name, target.DevicePath ?? string.Empty, target.IsInternal, (info.dwFlags & PrimaryFlag) != 0)));
        }
        return found;
    }

    private const uint PrimaryFlag = 1; // MONITORINFOF_PRIMARY
}
