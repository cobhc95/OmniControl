using OmniDial.Core.Interfaces;
using OmniDial.Core.Models;
#if WINDOWS
using OmniDial.Core.Native;
using System.ComponentModel;
using System.Runtime.InteropServices;
#endif

namespace OmniDial.Core.Engine;

/// <summary>Single physical DDC/CI monitor. Isolates all dxva2 calls; throttles rapid SetVCPFeature bursts.</summary>
public sealed class DdcCiMonitor : IMonitor
{
#if WINDOWS
    private readonly Dxva2Native.PHYSICAL_MONITOR _physical;
#endif
    private readonly object _gate = new();          // serialises VCP traffic
    private const int MinSetIntervalMs = 40;        // many EDIDs dislike &lt;30ms bursts
    private DateTime _lastSet = DateTime.MinValue;
    private bool _disposed;

    public MonitorInfo Info { get; }

#if WINDOWS
    internal DdcCiMonitor(MonitorInfo info, Dxva2Native.PHYSICAL_MONITOR physical)
    {
        Info = info;
        _physical = physical;
    }
#else
    internal DdcCiMonitor(MonitorInfo info) { Info = info; }
#endif

    // ---------------------------------------------------------------- VCP
    public OperationResult TryGetVcp(byte code, out VcpValue value)
    {
        value = VcpValue.Unsupported;
#if WINDOWS
        lock (_gate)
        {
            bool ok = Dxva2Native.GetVCPFeatureAndVCPFeatureReply(
                _physical.hPhysicalMonitor, code, out uint type, out uint cur, out uint max);
            if (!ok)
                return OperationResult.Fail($"GetVCP 0x{code:X2} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            value = new VcpValue(cur, max, type, type == 0);
            return OperationResult.Ok($"0x{code:X2} = {cur}/{max} type={type}");
        }
#else
        // Linux: open /dev/i2c-<n>, ioctl I2C_SLAVE + DDC/CI packet. Stubbed pending libddcutil port.
        return OperationResult.Fail("DDC/CI on Linux: implement via /dev/i2c-* (libddcutil) — stub.");
#endif
    }

    public VcpValue? GetVcp(byte code)
    {
        var r = TryGetVcp(code, out var v);
        return r.Success ? v : null;
    }

    public OperationResult TrySetVcp(byte code, uint value)
    {
#if WINDOWS
        lock (_gate)
        {
            var since = (DateTime.UtcNow - _lastSet).TotalMilliseconds;
            if (since < MinSetIntervalMs)
                Thread.Sleep((int)(MinSetIntervalMs - since));
            bool ok = Dxva2Native.SetVCPFeature(_physical.hPhysicalMonitor, code, value);
            _lastSet = DateTime.UtcNow;
            if (!ok)
                return OperationResult.Fail(
                    $"SetVCP 0x{code:X2}={value} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return OperationResult.Ok($"Set 0x{code:X2} = {value}");
        }
#else
        return OperationResult.Fail("DDC/CI on Linux: /dev/i2c path not implemented yet.");
#endif
    }

    public string? GetCapabilities()
    {
#if WINDOWS
        lock (_gate)
        {
            IntPtr pvct = IntPtr.Zero;
            try
            {
                bool ok = Dxva2Native.CapabilitiesRequestAndCapabilitiesReply(
                    "(vcp(10) vcp(12) vcp(60) vcp(8C) vcp(8E) vcp(90) vcp(CC))", 255, out pvct, out uint len);
                if (!ok || pvct == IntPtr.Zero) return null;
                string s = Marshal.PtrToStringUni(pvct, (int)len) ?? string.Empty;
                Info.Capabilities = s;
                return s;
            }
            catch { return null; }
            finally { if (pvct != IntPtr.Zero) Marshal.FreeCoTaskMem(pvct); }
        }
#else
        return null;
#endif
    }

    // --------------------------------------------------------- typed helpers
    public OperationResult SetBrightness(int v) => TrySetVcp(VcpCode.Brightness, (uint)v);
    public OperationResult SetContrast(int v)   => TrySetVcp(VcpCode.Contrast, (uint)v);
    public OperationResult SetSharpness(int v)  => TrySetVcp(VcpCode.Sharpness, (uint)v);
    public OperationResult SetRedGain(int v)    => TrySetVcp(VcpCode.RedGain, (uint)v);
    public OperationResult SetGreenGain(int v)  => TrySetVcp(VcpCode.GreenGain, (uint)v);
    public OperationResult SetBlueGain(int v)   => TrySetVcp(VcpCode.BlueGain, (uint)v);
    public OperationResult SetColorTemperature(int kelvin)
    {
        return TrySetVcp(VcpCode.ColorTemperature, kelvin >= 1000 ? 0x0Bu : 0x0Au);
    }
    public OperationResult SetInputSource(uint src) => TrySetVcp(VcpCode.InputSource, src);
    public OperationResult SetVolume(int v)    => TrySetVcp(VcpCode.Volume, (uint)v);
    public OperationResult SetPower(bool on)   => TrySetVcp(VcpCode.Power, on ? VcpCode.PowerMode.On : VcpCode.PowerMode.Off);
    public OperationResult FactoryReset()      => TrySetVcp(VcpCode.FactoryReset, 0x01);
    public OperationResult SetMute(bool mute)  => TrySetVcp(VcpCode.Mute, mute ? 1u : 0u);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if WINDOWS
        try { Dxva2Native.DestroyPhysicalMonitors(1, new[] { _physical }); } catch { }
#endif
        GC.SuppressFinalize(this);
    }
}
