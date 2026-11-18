using System.Runtime.InteropServices;
using static OmniControl.Core.Native.D3D11Native;
using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>One graphics adapter as DXGI lists it: its name, ids, LUID (unique until the next restart), driver version and monitors.</summary>
internal sealed record GraphicsAdapter(int Index, string Name, uint VendorId, uint DeviceId, long Luid, bool Software, string Driver,
    IReadOnlyList<string> Outputs, IReadOnlyList<ScreenRect> Screens)
{
    /// <summary>True when one of its attached monitors holds the whole <paramref name="area"/>.</summary>
    public bool Shows(ScreenRect area) => Screens.Any(s => area.X >= s.X && area.Y >= s.Y && area.Right <= s.Right && area.Bottom <= s.Bottom);

    public string LuidText => $"{(uint)(Luid >> 32):X8}:{(uint)Luid:X8}";

    public override string ToString() =>
        $"adapter {Index} \"{Name}\" vendor {VendorId:X4} device {DeviceId:X4} LUID {LuidText} driver {Driver}{(Software ? " (software)" : "")}" +
        $", monitors: {(Outputs.Count == 0 ? "none" : string.Join(", ", Outputs))}";
}

/// <summary>
/// The PC's graphics adapters for a recording: listed once per recording (and logged), the key that says whether the drivers changed
/// since a way of recording was remembered (<see cref="MachineKey"/>), the hardware encoders each adapter has (Media Foundation's
/// MFTEnum2 by adapter LUID), and a Direct3D 11 device on one adapter for the encoder. Never keeps anything open.
/// </summary>
internal static class GraphicsAdapters
{
    /// <summary>Every adapter DXGI lists, in its order; empty when DXGI cannot be asked (said in the log).</summary>
    public static IReadOnlyList<GraphicsAdapter> List(RecordingLog log)
    {
        var found = new List<GraphicsAdapter>();
        try
        {
            var iid = IidFactory1;
            Check(CreateDXGIFactory1(ref iid, out var factory));
            try
            {
                for (uint a = 0; EnumAdapters1(factory, a, out var adapter) == 0; a++)
                {
                    try
                    {
                        found.Add(Describe((int)a, adapter));
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            log.Line($"graphics adapters could not be listed: {RecordingLog.Describe(ex)}");
        }
        foreach (var adapter in found) log.Line(adapter.ToString());
        return found;
    }

    private static unsafe GraphicsAdapter Describe(int index, IntPtr adapter)
    {
        var desc = GetAdapterDesc1(adapter);
        var name = new string(desc.Description).TrimEnd('\0');
        var version = DriverVersion(adapter);
        var driver = version == 0 ? "unknown" : $"{version >> 48 & 0xFFFF}.{version >> 32 & 0xFFFF}.{version >> 16 & 0xFFFF}.{version & 0xFFFF}";
        var outputs = new List<string>();
        var screens = new List<ScreenRect>();
        for (uint o = 0; EnumOutputs(adapter, o, out var output) == 0; o++)
        {
            try
            {
                var od = GetOutputDesc(output);
                var device = new string(od.DeviceName).TrimEnd('\0');
                var r = od.Desktop;
                if (od.AttachedToDesktop != 0) screens.Add(new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                outputs.Add($"{device} ({r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}{(od.AttachedToDesktop == 0 ? " not attached" : "")}{(od.Rotation is RotationIdentity or RotationUnspecified ? "" : " turned")})");
            }
            finally
            {
                Release(output);
            }
        }
        return new GraphicsAdapter(index, name, desc.VendorId, desc.DeviceId, desc.Luid, (desc.Flags & AdapterSoftware) != 0, driver, outputs, screens);
    }

    /// <summary>
    /// What a remembered way of recording is tied to: every real adapter's vendor, device and driver version (not the LUID, which changes at
    /// every restart). A new driver, a card added or removed gives a new key, so the graphics card is tried again.
    /// </summary>
    public static string MachineKey(IReadOnlyList<GraphicsAdapter> adapters) =>
        string.Join(";", adapters.Where(a => !a.Software).Select(a => $"{a.VendorId:X4}:{a.DeviceId:X4}:{a.Driver}"));

    /// <summary>The names of the hardware encoders of <paramref name="subtype"/> (H.264 or HEVC) on one adapter; empty when none or Windows cannot say.</summary>
    public static IReadOnlyList<string> HardwareEncoders(long luid, Guid subtype)
    {
        var names = new List<string>();
        var output = new TypeInfo { Major = Video, Sub = subtype };
        IntPtr attributes = IntPtr.Zero;
        try
        {
            Check(MFCreateAttributes(out attributes, 1));
            SetLuid(attributes, AdapterLuid, luid);
            if (MFTEnum2(VideoEncoderCategory, MftEnumHardware | MftEnumSortAndFilter, IntPtr.Zero, ref output, attributes, out var activates, out var count) < 0)
                return names;
            for (var i = 0; i < count; i++)
            {
                var activate = Marshal.ReadIntPtr(activates, i * IntPtr.Size);
                names.Add(TryGetString(activate, FriendlyName) ?? "unnamed");
                Release(activate);
            }
            if (activates != IntPtr.Zero) CoTaskMemFree(activates);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            // an older Windows without MFTEnum2, or no Media Foundation: no per-adapter list, the chain still tries every way
        }
        finally
        {
            Release(attributes);
        }
        return names;
    }

    /// <summary>
    /// A Direct3D 11 device (with video support, shared with the encoder's own threads) on the adapter with <paramref name="luid"/>.
    /// Throws <see cref="COMException"/> or <see cref="InvalidOperationException"/> when there is none or it cannot be made.
    /// </summary>
    public static IntPtr CreateDevice(long luid)
    {
        var iid = IidFactory1;
        Check(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            for (uint a = 0; EnumAdapters1(factory, a, out var adapter) == 0; a++)
            {
                try
                {
                    if (GetAdapterDesc1(adapter).Luid != luid) continue;
                    Check(D3D11CreateDevice(adapter, DriverTypeUnknown, IntPtr.Zero, CreateBgraSupport | CreateVideoSupport, IntPtr.Zero, 0, SdkVersion,
                        out var device, out _, out var context));
                    Release(context);
                    var multithread = As(device, IidMultithread);
                    if (multithread == IntPtr.Zero)
                    {
                        Release(device);
                        throw new InvalidOperationException("the graphics card's device cannot be shared with the encoder");
                    }
                    SetMultithreadProtected(multithread, true);
                    Release(multithread);
                    return device;
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        throw new InvalidOperationException("that graphics adapter is gone");
    }
}
