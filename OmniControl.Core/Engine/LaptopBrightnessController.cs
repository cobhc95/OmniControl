using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
#if WINDOWS
using System.Management;
#endif

namespace OmniControl.Core.Engine;

/// <summary>Laptop LCD backlight — WMI on Windows, /sys/class/backlight on Linux.</summary>
public sealed class LaptopBrightnessController : ILaptopBrightness
{
#if WINDOWS
    private const string WmiScope = @"root\wmi";
    private const string BrightnessClass = "WmiMonitorBrightness";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";

    public bool IsAvailable => TryFindInstance(out _);

    public OperationResult GetBrightness(out int level, out int max)
    {
        level = 0; max = 100;
        using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {BrightnessClass}");
        foreach (ManagementObject obj in searcher.Get())
        {
            level = Convert.ToInt32(obj["CurrentBrightness"] ?? 50);
            max = Convert.ToInt32(obj["Levels"] is byte[] { Length: > 0 } arr ? arr[^1] : 100);
            return OperationResult.Ok($"LCD {level}/{max}");
        }
        return OperationResult.Fail("No WmiMonitorBrightness instance (desktop / no WMI support).");
    }

    public OperationResult SetBrightness(int level)
    {
        using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {MethodsClass}");
        foreach (ManagementObject obj in searcher.Get())
        {
            obj.InvokeMethod("WmiSetBrightness", new object[] { (ushort)1, (byte)Math.Clamp(level, 0, 100) });
            return OperationResult.Ok($"LCD set {level}");
        }
        return OperationResult.Fail("WmiMonitorBrightnessMethods not found.");
    }

    private bool TryFindInstance(out ManagementObject? result)
    {
        result = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {BrightnessClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                result = obj;
                return true;
            }
        }
        catch { }
        return false;
    }
#else
    // Linux: sysfs backlight — /sys/class/backlight/<dev>/brightness + max_brightness
    public bool IsAvailable => FindPanel() != null;

    public OperationResult GetBrightness(out int level, out int max)
    {
        level = 0; max = 100;
        var panel = FindPanel();
        if (panel == null) return OperationResult.Fail("No /sys/class/backlight device (desktop / no laptop panel).");
        try
        {
            int cur = int.Parse(File.ReadAllText(Path.Combine(panel, "brightness")).Trim());
            int m = int.Parse(File.ReadAllText(Path.Combine(panel, "max_brightness")).Trim());
            max = m; level = m == 0 ? 0 : (int)Math.Round(cur * 100.0 / m);
            return OperationResult.Ok($"LCD {level}/{max} (sysfs {cur}/{m})");
        }
        catch (Exception ex) { return OperationResult.Fail($"sysfs backlight: {ex.Message}"); }
    }

    public OperationResult SetBrightness(int level)
    {
        var panel = FindPanel();
        if (panel == null) return OperationResult.Fail("No /sys/class/backlight device.");
        try
        {
            int m = int.Parse(File.ReadAllText(Path.Combine(panel, "max_brightness")).Trim());
            int cur = m == 0 ? 0 : (int)Math.Round(Math.Clamp(level, 0, 100) * m / 100.0);
            File.WriteAllText(Path.Combine(panel, "brightness"), cur.ToString());
            return OperationResult.Ok($"LCD set {level}% (sysfs {cur}/{m})");
        }
        catch (Exception ex) { return OperationResult.Fail($"sysfs backlight: {ex.Message} (needs root or udev rule)."); }
    }

    private static string? FindPanel()
    {
        try
        {
            var root = "/sys/class/backlight";
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root).OrderBy(d => d).FirstOrDefault();
        }
        catch { return null; }
    }
#endif
}
