using System.Runtime.InteropServices;
using OmniControl.Core.Native;
using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>Where a recording's frames go: an MP4 (<see cref="Mp4Writer"/>) or a GIF (<see cref="GifWriter"/>).</summary>
internal interface IVideoSink : IDisposable
{
    /// <summary>Adds a BGRA frame (top row first) shown from <paramref name="at"/> on.</summary>
    void WriteFrame(ReadOnlySpan<byte> bgra, int stride, long at);

    /// <summary>The bytes in the file so far (for the size limit).</summary>
    long Length { get; }

    /// <summary>Ends the file at <paramref name="end"/>; it is complete and playable after this.</summary>
    void Finish(long end);
}

/// <summary>A sink that also takes sound (16-bit stereo PCM at <see cref="RecordingSound.SampleRate"/>).</summary>
internal interface ISoundSink
{
    void WriteSound(ReadOnlySpan<byte> pcm, long at, long duration);
}

/// <summary>
/// An MP4 written by Windows' Media Foundation Sink Writer: H.264 or HEVC video from BGRA frames (the Sink Writer converts the colours)
/// and AAC sound, on the graphics card's encoder when <c>hardware</c> is on and Windows has one. Two ways in: frames in memory
/// (<see cref="WriteFrame(ReadOnlySpan{byte}, int, long)"/>, from the GDI copy), or frames that stay on the graphics card
/// (<see cref="OpenOnGpu"/> + <see cref="WriteFrame(IGpuFrame, long)"/>: the Sink Writer gets the device through a DXGI device manager, so
/// the colour conversion and the encoder work on the textures and no frame is copied to or from memory). Frame samples come from a
/// <see cref="SamplePool"/>. Nothing is bundled: the encoders are Windows' own (HEVC needs the HEVC Video Extensions or a graphics card
/// that encodes it). Every COM object is released.
/// </summary>
internal sealed unsafe class Mp4Writer : IVideoSink, ISoundSink
{
    /// <summary>The AAC sound's bytes per second (192 kbit/s, one of the four rates Windows' AAC encoder takes).</summary>
    private const uint AacBytesPerSecond = 24000;

    private readonly string _path;
    private readonly int _width, _height;
    private readonly long _frameTicks;
    private IntPtr _writer, _manager;
    private readonly uint _video, _sound;
    private SamplePool? _pool;
    private bool _started;

    private Mp4Writer(string path, int width, int height, int frameRate, VideoFormat format, int bitrate, bool hardware, IGpuFrame? gpu)
    {
        _path = path;
        _width = width;
        _height = height;
        _frameTicks = VideoMath.FrameTicks(frameRate);
        Check(MFStartup(MfVersion, MfStartupFull));
        _started = true;
        try
        {
            if (gpu is not null) _manager = DeviceManager(gpu.Device);
            Check(MFCreateAttributes(out var attributes, 3));
            try
            {
                SetUInt32(attributes, EnableHardware, hardware ? 1u : 0u);
                if (_manager != IntPtr.Zero) SetUnknown(attributes, SinkWriterD3DManager, _manager);
                SetGuid(attributes, ContainerType, ContainerMp4);
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out _writer));
            }
            finally
            {
                Release(attributes);
            }
            _video = AddVideo(frameRate, format, bitrate);
            _sound = AddSound();
            BeginWriting(_writer);
        }
        catch
        {
            Dispose(); // nothing half-made stays open; the caller deletes the file
            throw;
        }
    }

    /// <summary>
    /// Opens the MP4 for frames in memory, on the graphics card's encoder first when <paramref name="hardware"/> is on and in software when
    /// that is refused (or the other way round when software was asked for and refused). Says in <paramref name="note"/> when the other way
    /// was used.
    /// </summary>
    public static Mp4Writer Open(string path, int width, int height, int frameRate, VideoFormat format, int bitrate, bool hardware, out string? note)
    {
        note = null;
        try
        {
            return new Mp4Writer(path, width, height, frameRate, format, bitrate, hardware, null);
        }
        catch (COMException)
        {
            note = hardware ? "The graphics card's encoder was not available, so the video was encoded in software." : "The software encoder was not available, so the graphics card's encoder was used.";
            return new Mp4Writer(path, width, height, frameRate, format, bitrate, !hardware, null);
        }
    }

    /// <summary>
    /// Opens the MP4 for frames that stay on <paramref name="gpu"/>'s graphics card, with its hardware encoder. Throws
    /// <see cref="COMException"/> when Media Foundation cannot take textures from that device (the caller then records from memory).
    /// </summary>
    public static Mp4Writer OpenOnGpu(string path, int width, int height, int frameRate, VideoFormat format, int bitrate, IGpuFrame gpu) =>
        new(path, width, height, frameRate, format, bitrate, true, gpu);

    /// <summary>A DXGI device manager holding <paramref name="device"/>, for the Sink Writer and the sample pool.</summary>
    private static IntPtr DeviceManager(IntPtr device)
    {
        Check(MFCreateDXGIDeviceManager(out var token, out var manager));
        try
        {
            ResetDevice(manager, device, token);
            return manager;
        }
        catch
        {
            Release(manager);
            throw;
        }
    }

    private uint AddVideo(int frameRate, VideoFormat format, int bitrate)
    {
        var output = NewType(Video, format == VideoFormat.Mp4Hevc ? Hevc : H264);
        try
        {
            SetUInt32(output, AvgBitrate, (uint)bitrate);
            SetFrame(output, frameRate);
            var stream = AddStream(_writer, output);
            var onGpu = _manager != IntPtr.Zero;
            var input = NewType(Video, onGpu ? Argb32 : Rgb32); // ARGB32 = the textures' BGRA format
            try
            {
                SetFrame(input, frameRate);
                if (!onGpu) SetUInt32(input, DefaultStride, (uint)(_width * 4)); // top row first, as the frames are grabbed
                SetInputMediaType(_writer, stream, input);
                _pool = Pool(input);
            }
            finally
            {
                Release(input);
            }
            return stream;
        }
        finally
        {
            Release(output);
        }
    }

    /// <summary>
    /// The pool of frame samples. On the graphics card it is required (a failure ends the opening, so the caller records from memory);
    /// in memory a pool Windows refuses only means a new buffer per frame, as before pooling.
    /// </summary>
    private SamplePool? Pool(IntPtr input)
    {
        if (_manager != IntPtr.Zero) return SamplePool.Create(input, _manager);
        try
        {
            return SamplePool.Create(input, IntPtr.Zero);
        }
        catch (COMException)
        {
            return null; // each frame gets a buffer of its own
        }
    }

    private void SetFrame(IntPtr type, int frameRate)
    {
        SetUInt32(type, InterlaceMode, InterlaceProgressive);
        SetPair(type, FrameSize, (uint)_width, (uint)_height);
        SetPair(type, FrameRate, (uint)frameRate, 1);
        SetPair(type, PixelAspectRatio, 1, 1);
    }

    private uint AddSound()
    {
        var output = NewType(Audio, Aac);
        try
        {
            SetSoundFormat(output);
            SetUInt32(output, AudioBytesPerSecond, AacBytesPerSecond);
            var stream = AddStream(_writer, output);
            var input = NewType(Audio, Pcm);
            try
            {
                SetSoundFormat(input);
                SetUInt32(input, AudioBlockAlign, RecordingSound.BytesPerFrame);
                SetUInt32(input, AudioBytesPerSecond, RecordingSound.SampleRate * RecordingSound.BytesPerFrame);
                SetInputMediaType(_writer, stream, input);
            }
            finally
            {
                Release(input);
            }
            return stream;
        }
        finally
        {
            Release(output);
        }
    }

    private static void SetSoundFormat(IntPtr type)
    {
        SetUInt32(type, AudioBits, 16);
        SetUInt32(type, AudioRate, RecordingSound.SampleRate);
        SetUInt32(type, AudioChannels, RecordingSound.Channels);
    }

    private static IntPtr NewType(Guid major, Guid sub)
    {
        Check(MFCreateMediaType(out var type));
        try
        {
            SetGuid(type, MajorType, major);
            SetGuid(type, SubType, sub);
            return type;
        }
        catch
        {
            Release(type);
            throw;
        }
    }

    public long Length => File.Exists(_path) ? new FileInfo(_path).Length : 0;

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, long at)
    {
        var sample = _pool?.Take() ?? IntPtr.Zero;
        if (sample == IntPtr.Zero)
        {
            WriteFreshFrame(bgra, stride, at); // no pool, or the encoder holds every pooled sample: a frame already copied is never dropped
            return;
        }
        try
        {
            var buffer = GetBufferByIndex(sample, 0);
            try
            {
                FillRows(buffer, bgra, stride);
            }
            finally
            {
                Release(buffer);
            }
            Stamp(sample, at, _frameTicks);
            WriteSample(_writer, _video, sample);
        }
        finally
        {
            Release(sample); // back to the pool once the encoder lets go of it too
        }
    }

    /// <summary>
    /// Hands the frame that is on the graphics card to the encoder: it is copied there into a pooled texture. False when every pooled
    /// texture is still with the encoder, so the frame is skipped (the video stays in time, as with any late frame).
    /// </summary>
    public bool WriteFrame(IGpuFrame frame, long at)
    {
        var sample = _pool!.Take();
        if (sample == IntPtr.Zero) return false;
        try
        {
            var buffer = GetBufferByIndex(sample, 0);
            try
            {
                CopyInto(buffer, frame);
                SetCurrentLength(buffer, ContiguousLength(buffer));
            }
            finally
            {
                Release(buffer);
            }
            Stamp(sample, at, _frameTicks);
            WriteSample(_writer, _video, sample);
            return true;
        }
        finally
        {
            Release(sample);
        }
    }

    private static void CopyInto(IntPtr buffer, IGpuFrame frame)
    {
        var dxgi = D3D11Native.As(buffer, IidDxgiBuffer);
        if (dxgi == IntPtr.Zero) throw new InvalidOperationException("The encoder's frame is not on the graphics card.");
        try
        {
            var texture = GetResource(dxgi, D3D11Native.IidTexture2D);
            try
            {
                frame.CopyTo(texture);
            }
            finally
            {
                Release(texture);
            }
        }
        finally
        {
            Release(dxgi);
        }
    }

    /// <summary>The bytes a frame takes in a pooled buffer (its 2-D length when it has one, else the packed size).</summary>
    private uint ContiguousLength(IntPtr buffer)
    {
        var twoD = D3D11Native.As(buffer, Iid2DBuffer);
        if (twoD == IntPtr.Zero) return (uint)(_width * 4 * _height);
        try
        {
            return GetContiguousLength(twoD);
        }
        finally
        {
            Release(twoD);
        }
    }

    /// <summary>Copies the frame's rows into a pooled memory buffer, following its own row pitch when it is a 2-D buffer.</summary>
    private void FillRows(IntPtr buffer, ReadOnlySpan<byte> bgra, int stride)
    {
        var twoD = D3D11Native.As(buffer, Iid2DBuffer);
        if (twoD == IntPtr.Zero)
        {
            CopyPacked(buffer, bgra, stride);
            SetCurrentLength(buffer, (uint)(_width * 4 * _height));
            return;
        }
        try
        {
            var row = _width * 4;
            var top = Lock2D(twoD, out var pitch);
            try
            {
                for (var y = 0; y < _height; y++) bgra.Slice(y * stride, row).CopyTo(new Span<byte>(top + (long)y * pitch, row));
            }
            finally
            {
                Unlock2D(twoD);
            }
            SetCurrentLength(buffer, GetContiguousLength(twoD));
        }
        finally
        {
            Release(twoD);
        }
    }

    private void CopyPacked(IntPtr buffer, ReadOnlySpan<byte> bgra, int stride)
    {
        var row = _width * 4;
        var target = new Span<byte>(Lock(buffer), row * _height);
        try
        {
            for (var y = 0; y < _height; y++) bgra.Slice(y * stride, row).CopyTo(target.Slice(y * row, row));
        }
        finally
        {
            Unlock(buffer);
        }
    }

    private void WriteFreshFrame(ReadOnlySpan<byte> bgra, int stride, long at)
    {
        var length = _width * 4 * _height;
        Check(MFCreateMemoryBuffer((uint)length, out var buffer));
        try
        {
            CopyPacked(buffer, bgra, stride);
            Send(_video, buffer, length, at, _frameTicks);
        }
        finally
        {
            Release(buffer);
        }
    }

    public void WriteSound(ReadOnlySpan<byte> pcm, long at, long duration)
    {
        Check(MFCreateMemoryBuffer((uint)pcm.Length, out var buffer));
        try
        {
            var target = new Span<byte>(Lock(buffer), pcm.Length);
            try
            {
                pcm.CopyTo(target);
            }
            finally
            {
                Unlock(buffer);
            }
            Send(_sound, buffer, pcm.Length, at, duration);
        }
        finally
        {
            Release(buffer);
        }
    }

    /// <summary>Wraps a filled buffer in a sample with its time and hands it to the Sink Writer.</summary>
    private void Send(uint stream, IntPtr buffer, int length, long at, long duration)
    {
        SetCurrentLength(buffer, (uint)length);
        Check(MFCreateSample(out var sample));
        try
        {
            AddBuffer(sample, buffer);
            Stamp(sample, at, duration);
            WriteSample(_writer, stream, sample);
        }
        finally
        {
            Release(sample);
        }
    }

    private static void Stamp(IntPtr sample, long at, long duration)
    {
        SetSampleTime(sample, at);
        SetSampleDuration(sample, duration);
    }

    public void Finish(long end) => FinalizeWriter(_writer);

    public void Dispose()
    {
        Release(_writer);
        _writer = IntPtr.Zero;
        _pool?.Dispose(); // after the writer, which gives its samples back as it goes
        _pool = null;
        Release(_manager);
        _manager = IntPtr.Zero;
        if (!_started) return;
        _started = false;
        MFShutdown();
    }
}
