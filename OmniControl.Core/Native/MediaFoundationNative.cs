using System.Runtime.InteropServices;
using NAudio.MediaFoundation;

namespace OmniControl.Core.Native;

/// <summary>
/// The Media Foundation calls of the screen recorder: the Sink Writer, media types, samples and buffers, and the encoder lookup. The COM
/// objects are raw pointers called through their vtables (NAudio keeps its own wrappers internal), so every one is released by the caller
/// with <see cref="Release"/>. Slot numbers count IUnknown's three methods first. GUIDs that NAudio publishes are taken from NAudio.
/// </summary>
internal static unsafe class MediaFoundationNative
{
    public const int MfVersion = 0x00020070, MfStartupFull = 0;
    public const uint InterlaceProgressive = 2;
    public const uint MftEnumSync = 0x1, MftEnumAsync = 0x2, MftEnumHardware = 0x4, MftEnumSortAndFilter = 0x40;

    public static readonly Guid MajorType = MediaFoundationAttributes.MF_MT_MAJOR_TYPE;
    public static readonly Guid SubType = MediaFoundationAttributes.MF_MT_SUBTYPE;
    public static readonly Guid AvgBitrate = MediaFoundationAttributes.MF_MT_AVG_BITRATE;
    public static readonly Guid AudioChannels = MediaFoundationAttributes.MF_MT_AUDIO_NUM_CHANNELS;
    public static readonly Guid AudioRate = MediaFoundationAttributes.MF_MT_AUDIO_SAMPLES_PER_SECOND;
    public static readonly Guid AudioBits = MediaFoundationAttributes.MF_MT_AUDIO_BITS_PER_SAMPLE;
    public static readonly Guid AudioBytesPerSecond = MediaFoundationAttributes.MF_MT_AUDIO_AVG_BYTES_PER_SECOND;
    public static readonly Guid AudioBlockAlign = MediaFoundationAttributes.MF_MT_AUDIO_BLOCK_ALIGNMENT;
    public static readonly Guid EnableHardware = MediaFoundationAttributes.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
    public static readonly Guid ContainerType = MediaFoundationAttributes.MF_TRANSCODE_CONTAINERTYPE;
    public static readonly Guid ContainerMp4 = TranscodeContainerTypes.MFTranscodeContainerType_MPEG4;
    public static readonly Guid Video = MediaTypes.MFMediaType_Video;
    public static readonly Guid Audio = MediaTypes.MFMediaType_Audio;
    public static readonly Guid Pcm = AudioSubtypes.MFAudioFormat_PCM;
    public static readonly Guid Aac = AudioSubtypes.MFAudioFormat_AAC;
    public static readonly Guid VideoEncoderCategory = MediaFoundationTransformCategories.VideoEncoder;

    public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid Hevc = new("43564548-0000-0010-8000-00aa00389b71");

    [StructLayout(LayoutKind.Sequential)]
    public struct TypeInfo
    {
        public Guid Major, Sub;
    }

    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);
    [DllImport("mfplat.dll")] public static extern int MFTEnumEx(Guid category, uint flags, IntPtr inputType, ref TypeInfo outputType, out IntPtr activates, out uint count);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr memory);

    /// <summary>Throws a <see cref="COMException"/> for a failed HRESULT.</summary>
    public static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    private static void** Vtbl(IntPtr obj) => *(void***)obj;

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(obj)[2])(obj);
    }

    // IMFAttributes (also the start of IMFMediaType and IMFSample)
    public static void SetUInt32(IntPtr obj, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(obj)[21])(obj, &key, value));

    public static void SetUInt64(IntPtr obj, Guid key, ulong value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(obj)[22])(obj, &key, value));

    public static void SetGuid(IntPtr obj, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(obj)[24])(obj, &key, &value));

    /// <summary>Two 32-bit numbers in one 64-bit attribute (a frame size, a frame rate, a pixel aspect ratio).</summary>
    public static void SetPair(IntPtr obj, Guid key, uint high, uint low) => SetUInt64(obj, key, (ulong)high << 32 | low);

    // IMFSample
    public static void SetSampleTime(IntPtr sample, long time) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[36])(sample, time));

    public static void SetSampleDuration(IntPtr sample, long duration) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[38])(sample, duration));

    public static void AddBuffer(IntPtr sample, IntPtr buffer) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(sample)[42])(sample, buffer));

    // IMFMediaBuffer
    public static byte* Lock(IntPtr buffer)
    {
        byte* data;
        uint max, current;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer)[3])(buffer, &data, &max, &current));
        return data;
    }

    public static void Unlock(IntPtr buffer) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(buffer)[4])(buffer));

    public static void SetCurrentLength(IntPtr buffer, uint length) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(buffer)[6])(buffer, length));

    // IMFSinkWriter
    public static uint AddStream(IntPtr writer, IntPtr type)
    {
        uint index;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(writer)[3])(writer, type, &index));
        return index;
    }

    public static void SetInputMediaType(IntPtr writer, uint stream, IntPtr type) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(writer)[4])(writer, stream, type, IntPtr.Zero));

    public static void BeginWriting(IntPtr writer) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[5])(writer));

    public static void WriteSample(IntPtr writer, uint stream, IntPtr sample) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Vtbl(writer)[6])(writer, stream, sample));

    public static void FinalizeWriter(IntPtr writer) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[11])(writer));
}
