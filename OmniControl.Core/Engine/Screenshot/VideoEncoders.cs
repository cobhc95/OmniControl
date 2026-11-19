using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// Which video encoders Windows has (asked of Media Foundation; nothing is opened or kept).
public static class VideoEncoders
{
    /// True when Windows has an HEVC encoder (the HEVC Video Extensions or a graphics card's own). Never throws.
    public static bool HevcAvailable()
    {
        var output = new TypeInfo { Major = Video, Sub = Hevc };
        try
        {
            if (MFTEnumEx(VideoEncoderCategory, MftEnumSync | MftEnumAsync | MftEnumHardware | MftEnumSortAndFilter, IntPtr.Zero, ref output,
                    out var activates, out var count) < 0) return false;
            for (var i = 0; i < count; i++) Release(Marshal.ReadIntPtr(activates, i * IntPtr.Size));
            if (activates != IntPtr.Zero) CoTaskMemFree(activates);
            return count > 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // a Windows without Media Foundation (an N edition without the Media Feature Pack)
        }
    }
}
