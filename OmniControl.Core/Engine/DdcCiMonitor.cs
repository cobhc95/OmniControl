using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>Single physical DDC/CI monitor. Isolates all dxva2 calls; throttles rapid SetVCPFeature bursts.</summary>
public sealed class DdcCiMonitor : IMonitor
{
    private readonly Dxva2Native.PHYSICAL_MONITOR _physical;
    private readonly object _gate = new();          // serialises VCP traffic
    private DateTime _lastSet = DateTime.MinValue;
    private MonitorCapabilities? _capabilities;
    private bool _disposed;

    public DisplayInfo Info { get; }

    internal DdcCiMonitor(DisplayInfo info, Dxva2Native.PHYSICAL_MONITOR physical)
    {
        Info = info;
        _physical = physical;
    }

    public OperationResult TryGetVcp(byte code, out VcpValue value)
    {
        value = VcpValue.Unsupported;
        lock (_gate)
        {
            if (_disposed) return OperationResult.Fail("The monitor is no longer attached.");
            bool ok = Dxva2Native.GetVCPFeatureAndVCPFeatureReply(
                _physical.hPhysicalMonitor, code, out uint type, out uint cur, out uint max);
            if (!ok)
                return OperationResult.Fail($"GetVCP 0x{code:X2} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            value = new VcpValue(cur, max, type, type == 0);
            return OperationResult.Ok($"0x{code:X2} = {cur}/{max} type={type}");
        }
    }

    public VcpValue? GetVcp(byte code)
    {
        var r = TryGetVcp(code, out var v);
        return r.Success ? v : null;
    }

    public OperationResult TrySetVcp(byte code, uint value)
    {
        lock (_gate)
        {
            if (_disposed) return OperationResult.Fail("The monitor is no longer attached.");
            var since = (DateTime.UtcNow - _lastSet).TotalMilliseconds;
            if (since < CoreTiming.DdcMinSetIntervalMs)
                Thread.Sleep((int)(CoreTiming.DdcMinSetIntervalMs - since));
            bool ok = Dxva2Native.SetVCPFeature(_physical.hPhysicalMonitor, code, value);
            _lastSet = DateTime.UtcNow;
            if (!ok)
                return OperationResult.Fail(
                    $"SetVCP 0x{code:X2}={value} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return OperationResult.Ok($"Set 0x{code:X2} = {value}");
        }
    }

    public MonitorCapabilities Probe()
    {
        lock (_gate) return _capabilities ??= MonitorProbe.Run(GetVcp, ReadCapabilitiesString);
    }

    /// <summary>The monitor's MCCS capabilities string, or null when it does not answer (many monitors do not, or answer after seconds).</summary>
    private string? ReadCapabilitiesString()
    {
        if (!Dxva2Native.GetCapabilitiesStringLength(_physical.hPhysicalMonitor, out var length) || length == 0) return null;
        var buffer = new byte[length];
        if (!Dxva2Native.CapabilitiesRequestAndCapabilitiesReply(_physical.hPhysicalMonitor, buffer, length)) return null;
        return Encoding.ASCII.GetString(buffer).TrimEnd('\0');
    }

    public void Dispose()
    {
        lock (_gate) // waits for a call in progress; later calls then fail instead of touching a released handle
        {
            if (_disposed) return;
            _disposed = true;
            Dxva2Native.DestroyPhysicalMonitors(1, new[] { _physical });
        }
        GC.SuppressFinalize(this);
    }
}
