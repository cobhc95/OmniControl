using System.Runtime.InteropServices;
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
/// and AAC sound, on the graphics card's encoder when <c>hardware</c> is on and Windows has one. Nothing is bundled: the encoders are
/// Windows' own (HEVC needs the HEVC Video Extensions or a graphics card that encodes it). Every COM object is released.
/// </summary>
internal sealed unsafe class Mp4Writer : IVideoSink, ISoundSink
{
    /// <summary>The AAC sound's bytes per second (192 kbit/s, one of the four rates Windows' AAC encoder takes).</summary>
    private const uint AacBytesPerSecond = 24000;

    private readonly string _path;
    private readonly int _width, _height;
    private readonly long _frameTicks;
    private IntPtr _writer;
    private readonly uint _video, _sound;
    private bool _started;

    private Mp4Writer(string path, int width, int height, int frameRate, VideoFormat format, int bitrate, bool hardware)
    {
        _path = path;
        _width = width;
        _height = height;
        _frameTicks = VideoMath.FrameTicks(frameRate);
        Check(MFStartup(MfVersion, MfStartupFull));
        _started = true;
        try
        {
            Check(MFCreateAttributes(out var attributes, 2));
            try
            {
                SetUInt32(attributes, EnableHardware, hardware ? 1u : 0u);
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
    /// Opens the MP4, on the graphics card first when <paramref name="hardware"/> is on and in software when that is refused (or the
    /// other way round when software was asked for and refused). Says in <paramref name="note"/> when the other way was used.
    /// </summary>
    public static Mp4Writer Open(string path, int width, int height, int frameRate, VideoFormat format, int bitrate, bool hardware, out string? note)
    {
        note = null;
        try
        {
            return new Mp4Writer(path, width, height, frameRate, format, bitrate, hardware);
        }
        catch (COMException)
        {
            note = hardware ? "The graphics card's encoder was not available, so the video was encoded in software." : "The software encoder was not available, so the graphics card's encoder was used.";
            return new Mp4Writer(path, width, height, frameRate, format, bitrate, !hardware);
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
            var input = NewType(Video, Rgb32);
            try
            {
                SetFrame(input, frameRate);
                SetUInt32(input, DefaultStride, (uint)(_width * 4)); // top row first, as the frames are grabbed
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
        var row = _width * 4;
        var length = row * _height;
        Check(MFCreateMemoryBuffer((uint)length, out var buffer));
        try
        {
            var target = new Span<byte>(Lock(buffer), length);
            try
            {
                for (var y = 0; y < _height; y++) bgra.Slice(y * stride, row).CopyTo(target.Slice(y * row, row));
            }
            finally
            {
                Unlock(buffer);
            }
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
            SetSampleTime(sample, at);
            SetSampleDuration(sample, duration);
            WriteSample(_writer, stream, sample);
        }
        finally
        {
            Release(sample);
        }
    }

    public void Finish(long end) => FinalizeWriter(_writer);

    public void Dispose()
    {
        Release(_writer);
        _writer = IntPtr.Zero;
        if (!_started) return;
        _started = false;
        MFShutdown();
    }
}
