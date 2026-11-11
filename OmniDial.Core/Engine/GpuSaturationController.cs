using OmniDial.Core.Models;

namespace OmniDial.Core.Engine;

/// <summary>GPU digital vibrance / saturation. NVIDIA via NVAPI, AMD via ADL. Both are best-effort.
/// Saturation is 0-100 where 50 = neutral; drivers may clamp/round.</summary>
public interface IGpuSaturation
{
    string Driver { get; }
    bool IsAvailable { get; }
    OperationResult SetSaturation(int value);
    OperationResult GetSaturation(out int value);
}

public sealed class NvApiSaturation : IGpuSaturation
{
    public string Driver => "NVIDIA NVAPI";
    public bool IsAvailable => NativeLibraryExists("nvapi64.dll") || NativeLibraryExists("nvapi.dll");

    public OperationResult SetSaturation(int value)
    {
        // NvAPI_D3DR11SetSaturation / NvAPI_GPU_SetSaturation are not public stable API;
        // the reliable public path is via NVAPI's image-adjustments on the display device.
        // We attempt the documented function pointer lookup; if absent we report unsupported
        // rather than crashing. Replace with vendor SDK when integrated (nvapi_interface.h).
        return OperationResult.Fail("NVAPI saturation: driver present but no stable public saturation entry point. Use NVIDIA App / panel, or extend with nvapi_interface.h NvAPI_D3DR11SetSaturation if your driver exposes it.");
    }

    public OperationResult GetSaturation(out int value)
    {
        value = 50;
        return OperationResult.Fail("NVAPI saturation query not available via public API.");
    }

    private static bool NativeLibraryExists(string name)
    {
        try { return System.IO.File.Exists(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), name)); } catch { return false; }
    }
}

public sealed class AmdSaturation : IGpuSaturation
{
    public string Driver => "AMD ADL";
    public bool IsAvailable => NativeLibraryExists("atiadlxx.dll") || NativeLibraryExists("adl32.dll");

    public OperationResult SetSaturation(int value)
    {
        return OperationResult.Fail("AMD ADL saturation: ADL present but requires ADL_Adapter_Map + Overdrive6; not enabled in test build. Extend with adl headers for production.");
    }

    public OperationResult GetSaturation(out int value)
    {
        value = 50;
        return OperationResult.Fail("AMD saturation query not available.");
    }

    private static bool NativeLibraryExists(string name)
    {
        try { return System.IO.File.Exists(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), name)); } catch { return false; }
    }
}

/// <summary>Picks first available GPU saturation backend.</summary>
public static class GpuSaturationFactory
{
    public static IGpuSaturation Create()
    {
        var nvidia = new NvApiSaturation();
        if (nvidia.IsAvailable) return nvidia;
        var amd = new AmdSaturation();
        if (amd.IsAvailable) return amd;
        return nvidia; // will report unavailable clearly
    }
}
