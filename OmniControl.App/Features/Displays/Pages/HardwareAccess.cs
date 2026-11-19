using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

/// How one display's own controls are read and written: over DDC/CI for a monitor, through Windows for a laptop's panel. Every call is slow; use them off the UI thread.
internal interface IHardwareAccess
{
    bool IsLaptopPanel { get; }

    /// The tooltip on a control this display does not have.
    string UnsupportedReason { get; }

    /// What the display supports (found once, then remembered).
    MonitorCapabilities Probe();

    /// A continuous control as a 0-100 share of its range; null when the display does not answer.
    int? ReadPercent(byte code);

    OperationResult WritePercent(byte code, int percent);

    /// A table-type control's current raw value (power mode); null when the display does not answer (a sleeping monitor often does not).
    uint? ReadRaw(byte code);

    /// A table-type control (input, colour preset, power) set to one of its values.
    OperationResult WriteRaw(byte code, uint value);
}

/// A monitor on DDC/CI.
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

    public uint? ReadRaw(byte code) => _monitor.GetVcp(code)?.Current;

    public OperationResult WriteRaw(byte code, uint value) => _monitor.TrySetVcp(code, value);
}

/// A laptop's own panel: Windows (WMI) offers only its brightness.
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
        return new MonitorCapabilities(features, new Dictionary<byte, IReadOnlyList<VcpChoice>>());
    }

    public int? ReadPercent(byte code) =>
        code == VcpCode.Brightness && _laptop.GetBrightness(out var level, out var max).Success && max > 0 ? Percent.Of(level, max) : null;

    public OperationResult WritePercent(byte code, int percent) =>
        code == VcpCode.Brightness ? _laptop.SetBrightness(percent) : OperationResult.Fail(UnsupportedReason);

    public uint? ReadRaw(byte code) => null;

    public OperationResult WriteRaw(byte code, uint value) => OperationResult.Fail(UnsupportedReason);
}
