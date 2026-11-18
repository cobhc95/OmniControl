using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>
/// The Direct3D 11 and DXGI calls of the screen recorder's graphics-card path: the adapters and their outputs, the device, Desktop
/// Duplication, textures, copies on the graphics card and a GDI device context on a texture (for the pointer). Raw vtable calls as in
/// <see cref="MediaFoundationNative"/> (slot numbers count IUnknown's three methods first, checked against the Windows SDK headers);
/// every object is released by the caller with <see cref="MediaFoundationNative.Release"/>.
/// </summary>
internal static unsafe class D3D11Native
{
    public const int SdkVersion = 7, DriverTypeUnknown = 0;
    public const uint CreateBgraSupport = 0x20, CreateVideoSupport = 0x800;
    public const uint FormatB8G8R8A8 = 87, UsageDefault = 0;
    public const uint BindShaderResource = 0x8, BindRenderTarget = 0x20, MiscGdiCompatible = 0x200;
    public const uint RotationUnspecified = 0, RotationIdentity = 1;

    public const int ErrorAccessLost = unchecked((int)0x887A0026),
        ErrorWaitTimeout = unchecked((int)0x887A0027),
        AccessDenied = unchecked((int)0x80070005); // E_ACCESSDENIED: the secure desktop (locked, UAC, Ctrl+Alt+Del) is up

    public static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static readonly Guid IidOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IidSurface1 = new("4AE63092-6327-4c1b-80AE-BFE12EA32B86");
    public static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IidMultithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");

    [StructLayout(LayoutKind.Sequential)]
    public struct OutputDesc
    {
        public fixed char DeviceName[32];
        public ScreenshotNative.Rect Desktop;
        public int AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
    }

    /// <summary>DXGI_ADAPTER_DESC1; <see cref="Flags"/> with <see cref="AdapterSoftware"/> = Microsoft's Basic Render Driver.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long Luid;
        public uint Flags;
    }

    public const uint AdapterSoftware = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct DuplicationDesc
    {
        public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, ScanlineOrdering, Scaling;
        public uint Rotation;
        public int DesktopImageInSystemMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FrameInfo
    {
        public long LastPresentTime, LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced, ProtectedContentMaskedOut;
        public int PointerX, PointerY, PointerVisible;
        public uint TotalMetadataBufferSize, PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    [DllImport("dxgi.dll")] public static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint levelCount, uint sdkVersion, out IntPtr device, out int level, out IntPtr context);

    private static void** Vtbl(IntPtr obj) => *(void***)obj;

    /// <summary>QueryInterface; zero when the object does not have that interface.</summary>
    public static IntPtr As(IntPtr obj, Guid iid) => Marshal.QueryInterface(obj, ref iid, out var result) == 0 ? result : IntPtr.Zero;

    // IDXGIFactory1
    public static int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter)
    {
        IntPtr found;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(factory)[12])(factory, index, &found);
        adapter = found;
        return hr;
    }

    // IDXGIAdapter
    public static int EnumOutputs(IntPtr adapter, uint index, out IntPtr output)
    {
        IntPtr found;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(adapter)[7])(adapter, index, &found);
        output = found;
        return hr;
    }

    // IDXGIAdapter1
    public static AdapterDesc1 GetAdapterDesc1(IntPtr adapter1)
    {
        AdapterDesc1 desc;
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)Vtbl(adapter1)[10])(adapter1, &desc));
        return desc;
    }

    /// <summary>The user-mode driver's version (four 16-bit parts, highest first), or 0 when the adapter will not say.</summary>
    public static long DriverVersion(IntPtr adapter)
    {
        var iid = IidDxgiDevice;
        long version;
        return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, long*, int>)Vtbl(adapter)[9])(adapter, &iid, &version) >= 0 ? version : 0;
    }

    // IDXGIOutput
    public static OutputDesc GetOutputDesc(IntPtr output)
    {
        OutputDesc desc;
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, OutputDesc*, int>)Vtbl(output)[7])(output, &desc));
        return desc;
    }

    // IDXGIOutput1
    public static IntPtr DuplicateOutput(IntPtr output1, IntPtr device)
    {
        IntPtr duplication;
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(output1)[22])(output1, device, &duplication));
        return duplication;
    }

    // IDXGIOutputDuplication
    public static DuplicationDesc GetDuplicationDesc(IntPtr duplication)
    {
        DuplicationDesc desc;
        ((delegate* unmanaged[Stdcall]<IntPtr, DuplicationDesc*, void>)Vtbl(duplication)[7])(duplication, &desc);
        return desc;
    }

    public static int AcquireNextFrame(IntPtr duplication, uint timeoutMs, out IntPtr resource)
    {
        FrameInfo info;
        IntPtr found;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, FrameInfo*, IntPtr*, int>)Vtbl(duplication)[8])(duplication, timeoutMs, &info, &found);
        resource = found;
        return hr;
    }

    public static int ReleaseFrame(IntPtr duplication) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(duplication)[14])(duplication);

    // ID3D11Device
    public static IntPtr CreateTexture2D(IntPtr device, Texture2DDesc desc)
    {
        IntPtr texture;
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, IntPtr, IntPtr*, int>)Vtbl(device)[5])(device, &desc, IntPtr.Zero, &texture));
        return texture;
    }

    // ID3D11DeviceContext
    public static void CopySubresourceRegion(IntPtr context, IntPtr target, IntPtr source, Box box) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Vtbl(context)[46])(context, target, 0, 0, 0, 0, source, 0, &box);

    public static void CopyResource(IntPtr context, IntPtr target, IntPtr source) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Vtbl(context)[47])(context, target, source);

    // ID3D10Multithread
    public static void SetMultithreadProtected(IntPtr multithread, bool on) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Vtbl(multithread)[5])(multithread, on ? 1 : 0);

    // IDXGISurface1
    public static IntPtr GetDC(IntPtr surface1)
    {
        IntPtr hdc;
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Vtbl(surface1)[11])(surface1, 0, &hdc)); // 0: keep what is there
        return hdc;
    }

    public static void ReleaseDC(IntPtr surface1) =>
        MediaFoundationNative.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(surface1)[12])(surface1, IntPtr.Zero));
}
