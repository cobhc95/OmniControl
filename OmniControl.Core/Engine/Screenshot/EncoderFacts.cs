using OmniControl.Core.Native;
using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// The video encoder the Sink Writer chose for a recording, read back after it started writing (IMFSinkWriterEx): its name, whether it is
/// a graphics card's (hardware) and asynchronous, the adapter it is bound to when it says (<c>MFT_ENUM_ADAPTER_LUID</c>), and the formats
/// and size it was set to. The Sink Writer drives the encoder itself, asynchronous hardware encoders included (it unlocks and feeds them
/// on their events), so OmniControl never calls the encoder; these facts are for the log and for the same-adapter check.
/// </summary>
internal sealed record EncoderFacts(string Name, bool Hardware, bool Async, long? Luid, string Input, string Output)
{
    public override string ToString() =>
        $"encoder \"{Name}\" {(Hardware ? "hardware" : "software")}, {(Async ? "async" : "sync")}, adapter LUID {(Luid is { } l ? $"{(uint)(l >> 32):X8}:{(uint)l:X8}" : "not given")}, in {Input}, out {Output}";

    /// <summary>The encoder of <paramref name="stream"/>, or null when Windows will not say (an older Windows, or no encoder in the chain).</summary>
    public static EncoderFacts? Read(IntPtr writer, uint stream)
    {
        var writerEx = D3D11Native.As(writer, IidSinkWriterEx);
        if (writerEx == IntPtr.Zero) return null;
        try
        {
            for (uint index = 0; index < 8; index++)
            {
                if (GetTransformForStream(writerEx, stream, index, out var category, out var transform) < 0) return null;
                try
                {
                    if (category == VideoEncoderCategory) return Of(transform);
                }
                finally
                {
                    Release(transform);
                }
            }
            return null;
        }
        finally
        {
            Release(writerEx);
        }
    }

    private static EncoderFacts Of(IntPtr transform)
    {
        var attributes = TransformAttributes(transform);
        try
        {
            var name = attributes == IntPtr.Zero ? null : TryGetString(attributes, FriendlyName)
                       ?? (TryGetGuid(attributes, TransformClsid, out var clsid) >= 0 ? $"CLSID {clsid}" : null);
            var hardware = attributes != IntPtr.Zero && TryGetString(attributes, HardwareUrl) is not null;
            var async = attributes != IntPtr.Zero && TryGetUInt32(attributes, TransformAsync, out var a) >= 0 && a != 0;
            var luid = attributes == IntPtr.Zero ? null : TryGetLuid(attributes, AdapterLuid);
            return new EncoderFacts(name ?? "(name not given)", hardware, async, luid, TypeText(transform, false), TypeText(transform, true));
        }
        finally
        {
            Release(attributes);
        }
    }

    /// <summary>"NV12 3840x2160 @60" for a current media type, or "not set".</summary>
    private static string TypeText(IntPtr transform, bool output)
    {
        var type = TransformCurrentType(transform, output);
        if (type == IntPtr.Zero) return "not set";
        try
        {
            var sub = TryGetGuid(type, SubType, out var s) >= 0 ? FourCc(s) : "?";
            var size = TryGetUInt64(type, FrameSize, out var f) >= 0 ? $" {f >> 32}x{(uint)f}" : "";
            var rate = TryGetUInt64(type, FrameRate, out var r) >= 0 && (uint)r != 0 ? $" @{(double)(r >> 32) / (uint)r:0.##}" : "";
            return sub + size + rate;
        }
        finally
        {
            Release(type);
        }
    }

    /// <summary>A video subtype's four letters (NV12, H264, HEVC), or its number for the RGB ones Media Foundation numbers (21 = ARGB32, 22 = RGB32).</summary>
    private static string FourCc(Guid subtype)
    {
        var code = BitConverter.ToUInt32(subtype.ToByteArray(), 0);
        if (code < 0x100) return code == 21 ? "ARGB32" : code == 22 ? "RGB32" : code.ToString();
        var chars = new[] { (char)(code & 0xFF), (char)(code >> 8 & 0xFF), (char)(code >> 16 & 0xFF), (char)(code >> 24 & 0xFF) };
        return new string(chars);
    }
}
