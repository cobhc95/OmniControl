using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>Enumerates DDC/CI monitors — EnumDisplayMonitors + dxva2.</summary>
public sealed class MonitorEnumerator : IMonitorEnumerator
{
    public List<IMonitor> Enumerate()
    {
        var monitors = new List<IMonitor>();
        var list = new List<IntPtr>();

        var proc = new Dxva2Native.MonitorEnumProc((hMonitor, hdc, lParam, dwData) =>
        {
            list.Add(hMonitor);
            return true;
        });

        Dxva2Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);

        int index = 0;
        foreach (var hMonitor in list)
        {
            var infoEx = new Dxva2Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Dxva2Native.MONITORINFOEX)) };
            Dxva2Native.GetMonitorInfo(hMonitor, ref infoEx);
            string deviceName = infoEx.szDevice ?? $"DISPLAY{index + 1}";

            var physical = new Dxva2Native.PHYSICAL_MONITOR[1];
            bool ok = Dxva2Native.GetPhysicalMonitorsFromHMONITOR(hMonitor, 1, physical);
            if (!ok || physical[0].hPhysicalMonitor == IntPtr.Zero)
                continue;

            string desc = physical[0].szPhysicalMonitorDescription ?? deviceName;
            string friendly = TryGetFriendlyName(deviceName);

            var mi = new MonitorInfo
            {
                Index = index,
                Description = desc,
                DeviceName = deviceName,
                FriendlyName = string.IsNullOrWhiteSpace(friendly) ? desc : friendly,
                InstanceId = deviceName,
            };

            monitors.Add(new DdcCiMonitor(mi, physical[0]));
            index++;
        }
        GC.KeepAlive(proc);
        return monitors.Cast<IMonitor>().ToList();
    }

    private static string TryGetFriendlyName(string deviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (key == null) return string.Empty;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(sub);
                if (subKey == null) continue;
                foreach (var ctlKeyName in subKey.GetSubKeyNames())
                {
                    using var ctl = subKey.OpenSubKey(ctlKeyName);
                    if (ctl?.GetValue("DeviceDesc") is string desc && desc != string.Empty)
                    {
                        string s = desc.TrimStart('@').Trim();
                        var semi = s.LastIndexOf(';');
                        if (semi >= 0) s = s[(semi + 1)..];
                        var slash = s.LastIndexOf('\\');
                        if (slash >= 0) s = s[(slash + 1)..];
                        s = s.Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }
        }
        catch { /* registry description unreadable: caller falls back to a generic name */ }
        return string.Empty;
    }
}
