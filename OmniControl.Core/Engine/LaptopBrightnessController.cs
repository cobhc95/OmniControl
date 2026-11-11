using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
using System.Management;

namespace OmniControl.Core.Engine;

/// <summary>Laptop LCD backlight — via WMI.</summary>
public sealed class LaptopBrightnessController : ILaptopBrightness
{
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
        catch { /* no WMI brightness class: not a laptop panel */ }
        return false;
    }
}
