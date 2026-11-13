using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>How one display's own controls are read and written: over DDC/CI for a monitor, through Windows for a laptop's panel. Every call is slow; use them off the UI thread.</summary>
internal interface IHardwareAccess
{
    bool IsLaptopPanel { get; }

    /// <summary>The tooltip on a control this display does not have.</summary>
    string UnsupportedReason { get; }

    /// <summary>What the display supports (found once, then remembered).</summary>
    MonitorCapabilities Probe();

    /// <summary>A continuous control as a 0-100 share of its range; null when the display does not answer.</summary>
    int? ReadPercent(byte code);

    OperationResult WritePercent(byte code, int percent);

    /// <summary>A table-type control (input, colour preset, power) set to one of its values.</summary>
    OperationResult WriteRaw(byte code, uint value);
}

/// <summary>A monitor on DDC/CI.</summary>
internal sealed class MonitorAccess : IHardwareAccess
{
    private readonly IMonitor _monitor;

    public MonitorAccess(IMonitor monitor) => _monitor = monitor;

    public bool IsLaptopPanel => false;
    public string UnsupportedReason => "Not reported by this monitor";

    public MonitorCapabilities Probe() => _monitor.Probe();

    public int? ReadPercent(byte code) => _monitor.GetVcp(code) is { Maximum: > 0 } v ? v.Percent : null;

    public OperationResult WritePercent(byte code, int percent) =>
        _monitor.TrySetVcp(code, Percent.ToRaw(percent, _monitor.Probe().Level(code)?.Maximum ?? Percent.Max));

    public OperationResult WriteRaw(byte code, uint value) => _monitor.TrySetVcp(code, value);
}

/// <summary>A laptop's own panel: Windows (WMI) offers only its brightness.</summary>
internal sealed class LaptopAccess : IHardwareAccess
{
    private readonly ILaptopBrightness _laptop;

    public LaptopAccess(ILaptopBrightness laptop) => _laptop = laptop;

    public bool IsLaptopPanel => true;
    public string UnsupportedReason => "A laptop's own panel has only brightness";

    public MonitorCapabilities Probe()
    {
        if (!_laptop.GetBrightness(out var level, out var max).Success || max <= 0) return MonitorCapabilities.None;
        var features = new Dictionary<byte, VcpValue> { [VcpCode.Brightness] = new((uint)level, (uint)max, 2, false) };
        return new MonitorCapabilities(features, Array.Empty<VcpChoice>(), Array.Empty<VcpChoice>());
    }

    public int? ReadPercent(byte code) =>
        code == VcpCode.Brightness && _laptop.GetBrightness(out var level, out var max).Success && max > 0 ? Percent.Of(level, max) : null;

    public OperationResult WritePercent(byte code, int percent) =>
        code == VcpCode.Brightness ? _laptop.SetBrightness(percent) : OperationResult.Fail(UnsupportedReason);

    public OperationResult WriteRaw(byte code, uint value) => OperationResult.Fail(UnsupportedReason);
}
