using OmniDial.Core.Models;

namespace OmniDial.Core.Interfaces;

/// <summary>Portable monitor abstraction — implemented per-OS (dxva2 on Windows, I2C on Linux).</summary>
public interface IMonitor : IDisposable
{
    MonitorInfo Info { get; }
    OperationResult TryGetVcp(byte code, out VcpValue value);
    VcpValue? GetVcp(byte code);
    OperationResult TrySetVcp(byte code, uint value);
    string? GetCapabilities();
    OperationResult SetBrightness(int v);
    OperationResult SetContrast(int v);
    OperationResult SetSharpness(int v);
    OperationResult SetRedGain(int v);
    OperationResult SetGreenGain(int v);
    OperationResult SetBlueGain(int v);
    OperationResult SetColorTemperature(int kelvin);
    OperationResult SetInputSource(uint src);
    OperationResult SetVolume(int v);
    OperationResult SetPower(bool on);
    OperationResult FactoryReset();
    OperationResult SetMute(bool mute);
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

public interface ISystemVolume
{
    OperationResult GetVolume(out int percent);
    OperationResult SetVolume(int percent);
}

public interface IMonitorEngine : IDisposable
{
    List<IMonitor> Monitors { get; }
    ILaptopBrightness Laptop { get; }
    ISystemVolume System { get; }
    OmniDial.Core.Engine.IGpuSaturation Gpu { get; }
    void RefreshMonitors();
    OperationResult ExecuteCommand(string command, Action<string>? log = null);
}
