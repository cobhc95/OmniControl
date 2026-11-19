using OmniControl.Core.Interfaces;
using System.Management;

namespace OmniControl.Core.Engine;

/// Laptop LCD backlight — via WMI. Every WMI object a call reads is disposed before it returns.
public sealed class LaptopBrightnessController : ILaptopBrightness
{
    private const string WmiScope = @"root\wmi";
    private const string BrightnessClass = "WmiMonitorBrightness";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {BrightnessClass}");
                using var found = searcher.Get();
                foreach (var obj in found)
                {
                    obj.Dispose();
                    return true;
                }
            }
            catch (ManagementException) { /* no WMI brightness class: not a laptop panel */ }
            return false;
        }
    }

    public OperationResult GetBrightness(out int level, out int max)
    {
        level = 0; max = 100;
        using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {BrightnessClass}");
        using var found = searcher.Get();
        foreach (var obj in found)
        {
            using (obj)
            {
                level = Convert.ToInt32(obj["CurrentBrightness"] ?? 50);
                max = Convert.ToInt32(obj["Levels"] is byte[] { Length: > 0 } arr ? arr[^1] : 100);
                return OperationResult.Ok($"LCD {level}/{max}");
            }
        }
        return OperationResult.Fail("No WmiMonitorBrightness instance (desktop / no WMI support).");
    }

    public OperationResult SetBrightness(int level)
    {
        using var searcher = new ManagementObjectSearcher(WmiScope, $"SELECT * FROM {MethodsClass}");
        using var found = searcher.Get();
        foreach (ManagementObject obj in found)
        {
            using (obj)
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { (ushort)1, (byte)Percent.Clamp(level) });
                return OperationResult.Ok($"LCD set {level}");
            }
        }
        return OperationResult.Fail("WmiMonitorBrightnessMethods not found.");
    }
}
