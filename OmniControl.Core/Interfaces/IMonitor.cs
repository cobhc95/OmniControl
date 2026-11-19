namespace OmniControl.Core.Interfaces;

/// A monitor reached over DDC/CI (dxva2). All calls are slow (tens of milliseconds each): call them off the UI thread.
public interface IMonitor : IDisposable
{
    DisplayInfo Info { get; }
    OperationResult TryGetVcp(byte code, out VcpValue value);
    VcpValue? GetVcp(byte code);
    OperationResult TrySetVcp(byte code, uint value);

    /// What the monitor supports. The first call probes it (about a dozen reads, and the capabilities string when the monitor has
    /// inputs or colour presets to list) and the answer is kept, so later calls are free. Slow once: call it off the UI thread.
    MonitorCapabilities Probe();
}

public interface IMonitorEnumerator
{
    List<IMonitor> Enumerate();
}

public interface ILaptopBrightness
{
    bool IsAvailable { get; }
    OperationResult GetBrightness(out int level, out int max);
    OperationResult SetBrightness(int level);
}

/// How a display is turned (the order matches the choices shown to the user).
public enum DisplayOrientation { Landscape, Portrait, LandscapeFlipped, PortraitFlipped }

/// Reads and turns a display through Windows, whether or not it speaks DDC/CI. Slow: call off the UI thread.
public interface IDisplayRotation
{
    /// The display's orientation now; null when Windows would not say.
    DisplayOrientation? Read(string deviceName);

    OperationResult Set(string deviceName, DisplayOrientation orientation);
}

public interface IMonitorEngine : IDisposable
{
    /// The DDC/CI monitors now attached; replaced as a whole when the set changes, so a held list stays valid (its monitors are disposed).
    IReadOnlyList<IMonitor> Monitors { get; }

    /// Every attached display, with or without DDC/CI (the laptop's own panel included).
    IReadOnlyList<DisplayInfo> Displays { get; }

    ILaptopBrightness Laptop { get; }

    IDisplayRotation Rotation { get; }

    /// The name Windows gives a display now (looked up fresh, by its device path); null when it is not attached. Cheap.
    string? ResolveDeviceName(DisplayInfo remembered);

    /// Opens every monitor again, dropping what was learned about them. Slow.
    void RefreshMonitors();

    /// Looks at which displays are attached and reopens the monitors only when that changed. Cheap. True when it changed.
    bool Rescan();

    /// True when the displays Windows lists now (how many, which, named how) differ from <see cref="Displays"/>. Cheap: no monitor is talked to.
    bool DisplaysChanged();
}
