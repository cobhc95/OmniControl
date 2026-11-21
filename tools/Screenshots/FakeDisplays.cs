using OmniControl.Core.Interfaces;

namespace OmniControl.Screenshots;

/// Sample displays for the Displays page: two monitors that answer DDC/CI from memory (nothing is sent to a real monitor), no laptop panel,
/// rotation read as landscape and never set, gamma ramps that are never written.
internal sealed class FakeEngine : IMonitorEngine
{
    private static readonly DisplayInfo Main = new(@"\\.\SAMPLE1", "Studio 27", "sample-1", IsInternal: false, IsPrimary: true);
    private static readonly DisplayInfo Side = new(@"\\.\SAMPLE2", "Office 24", "sample-2", IsInternal: false, IsPrimary: false);

    public IReadOnlyList<IMonitor> Monitors { get; } = new IMonitor[]
    {
        new FakeMonitor(Main, brightness: 70, contrast: 55, inputs: new[] { (0x0Fu, "DisplayPort 1"), (0x11u, "HDMI 1"), (0x12u, "HDMI 2") }),
        new FakeMonitor(Side, brightness: 45, contrast: 50, inputs: new[] { (0x11u, "HDMI 1"), (0x0Fu, "DisplayPort 1") }),
    };

    public IReadOnlyList<DisplayInfo> Displays { get; } = new[] { Main, Side };
    public ILaptopBrightness Laptop { get; } = new NoLaptop();
    public IDisplayRotation Rotation { get; } = new Landscape();
    public string? ResolveDeviceName(DisplayInfo remembered) => Displays.FirstOrDefault(d => d.DevicePath == remembered.DevicePath)?.DeviceName;
    public void RefreshMonitors() { }
    public bool Rescan() => false;
    public bool DisplaysChanged() => false;
    public void Dispose() { }

    private sealed class NoLaptop : ILaptopBrightness
    {
        public bool IsAvailable => false;

        public OperationResult GetBrightness(out int level, out int max)
        {
            (level, max) = (0, 0);
            return OperationResult.Fail("No built-in panel.");
        }

        public OperationResult SetBrightness(int level) => OperationResult.Fail("No built-in panel.");
    }

    private sealed class Landscape : IDisplayRotation
    {
        public DisplayOrientation? Read(string deviceName) => DisplayOrientation.Landscape;
        public OperationResult Set(string deviceName, DisplayOrientation orientation) => OperationResult.Fail("Sample display.");
    }
}

/// A monitor that keeps its values in memory.
internal sealed class FakeMonitor : IMonitor
{
    private readonly Dictionary<byte, VcpValue> _values;
    private readonly MonitorCapabilities _capabilities;

    public FakeMonitor(DisplayInfo info, uint brightness, uint contrast, (uint Value, string Name)[] inputs)
    {
        Info = info;
        _values = new Dictionary<byte, VcpValue>
        {
            [VcpCode.Brightness] = new(brightness, 100, 2, false),
            [VcpCode.Contrast] = new(contrast, 100, 2, false),
            [VcpCode.RedGain] = new(100, 100, 2, false),
            [VcpCode.GreenGain] = new(98, 100, 2, false),
            [VcpCode.BlueGain] = new(94, 100, 2, false),
            [VcpCode.Sharpness] = new(50, 100, 2, false),
            [VcpCode.SpeakerVolume] = new(30, 100, 2, false),
            [VcpCode.ColorPreset] = new(0x05, 0x0B, 2, false),
            [VcpCode.InputSource] = new(inputs[0].Value, 0x12, 2, false),
            [VcpCode.PowerMode] = new(VcpCode.Power.On, 5, 2, false),
        };
        _capabilities = new MonitorCapabilities(_values, new Dictionary<byte, IReadOnlyList<VcpChoice>>
        {
            [VcpCode.InputSource] = inputs.Select(i => new VcpChoice(i.Value, i.Name)).ToList(),
            [VcpCode.ColorPreset] = new[] { new VcpChoice(0x01, "sRGB"), new VcpChoice(0x05, "6500 K"), new VcpChoice(0x0B, "User 1") },
        });
    }

    public DisplayInfo Info { get; }

    public OperationResult TryGetVcp(byte code, out VcpValue value)
    {
        lock (_values)
        {
            if (_values.TryGetValue(code, out value)) return OperationResult.Ok();
        }
        return OperationResult.Fail("Not supported.");
    }

    public VcpValue? GetVcp(byte code) => TryGetVcp(code, out var value).Success ? value : null;

    public OperationResult TrySetVcp(byte code, uint value)
    {
        lock (_values)
        {
            if (!_values.TryGetValue(code, out var old)) return OperationResult.Fail("Not supported.");
            _values[code] = old with { Current = value };
        }
        return OperationResult.Ok();
    }

    /// Answers after a pause like a real monitor's DDC/CI reads, so the orientation (read at once) is known when the cards are laid out.
    public MonitorCapabilities Probe()
    {
        Thread.Sleep(400);
        return _capabilities;
    }

    public void Dispose() { }
}

/// A display ramp that is never read from or written to Windows.
internal sealed class FakeGamma : IGammaDisplay
{
    public ushort[]? Original { get; private set; }
    public bool DriverRefuses => false;

    public OperationResult Capture()
    {
        Original = new ushort[768];
        return OperationResult.Ok();
    }

    public OperationResult Apply(SoftwareColor color) => OperationResult.Ok();
}
