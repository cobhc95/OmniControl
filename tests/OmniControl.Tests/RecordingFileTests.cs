using System.Runtime.InteropServices;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// <summary>
/// The real recorder end to end: a small corner of the screen is recorded for a few seconds to a file in the temp folder (MP4 with the
/// hardware encoder, MP4 in software, GIF), Stop must finish the file within <see cref="Timing.RecordingStopWait"/>, and the MP4 must be a
/// complete file (ftyp, moov and mdat boxes) that Media Foundation opens, with a duration and video frames. Nothing on the PC is changed;
/// the files are deleted.
/// </summary>
[Collection(nameof(RecordingFileTests))] // one recording at a time: they share the screen copy and the encoder
public class RecordingFileTests
{
    private static readonly TimeSpan RecordFor = TimeSpan.FromSeconds(2.5);

    private static RecordingRequest Request(string path, VideoFormat format, bool hardware, bool systemSound = false, int frameRate = 30, bool pointer = false) =>
        new(new ScreenRect(0, 0, 128, 96), path, format, frameRate, VideoQuality.Low, hardware, systemSound, false,
            new PointerLook(pointer, pointer, 0xFFC000, 12, 0.4), new RecordingLimits(TimeSpan.FromMinutes(1), 1L << 30, 0));

    private static async Task<(RecordingResult Result, List<string> Log)> Record(VideoFormat format, bool hardware, bool systemSound = false, int frameRate = 30, bool pointer = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rec-test-{Guid.NewGuid():N}{VideoMath.Extension(format)}");
        var log = new List<string>();
        var (recorder, _) = await ScreenRecorder.StartAsync(Request(path, format, hardware, systemSound, frameRate, pointer), line => { lock (log) log.Add(line); });
        await Task.Delay(RecordFor);
        recorder.Stop();
        var result = await recorder.Completion.WaitAsync(Timing.RecordingStopWait);
        return (result, log);
    }

    [Theory]
    [InlineData(true, false, 30, false)]
    [InlineData(false, false, 30, false)]
    [InlineData(true, true, 60, true)] // the owner's settings: hardware, system sound, 60 fps, pointer shown
    [InlineData(false, true, 60, true)]
    public async Task Mp4_StopFinishesAPlayableFile(bool hardware, bool systemSound, int frameRate, bool pointer)
    {
        var (result, log) = await Record(VideoFormat.Mp4H264, hardware, systemSound, frameRate, pointer);
        try
        {
            Assert.True(result.End == RecordingEnd.Stopped, $"{result.End}: {result.Message} | {string.Join(" | ", log)}");
            Assert.NotNull(result.Path);
            AssertMp4Boxes(result.Path!);
            var (duration, frames) = Mp4Probe.Read(result.Path!);
            Assert.InRange(duration.TotalSeconds, RecordFor.TotalSeconds - 0.5, RecordFor.TotalSeconds + 0.8); // no gap at the start while the encoder opens
            Assert.True(frames >= 20, $"only {frames} video frames");
        }
        finally
        {
            if (result.Path is { } p) File.Delete(p);
        }
    }

    [Fact]
    public void Mp4_HardwareEncoderFromMemoryFinishesAPlayableFile() => WriteDirect(true);

    [Fact]
    public void Mp4_SoftwareEncoderFromMemoryFinishesAPlayableFile() => WriteDirect(false);

    /// <summary>The MP4 writer alone, fed made-up frames and silence on this thread: isolates the encoder from the recorder's loop.</summary>
    private static void WriteDirect(bool hardware)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rec-direct-{Guid.NewGuid():N}.mp4");
        const int width = 128, height = 96, rate = 30, count = 60;
        var frame = new byte[width * height * 4];
        var pcm = new byte[RecordingSound.SampleRate / rate * RecordingSound.BytesPerFrame];
        var frameTicks = VideoMath.FrameTicks(rate);
        try
        {
            using (var writer = Mp4Writer.Open(path, width, height, rate, VideoFormat.Mp4H264, VideoMath.MinBitrate, hardware, new RecordingLog(_ => { })))
            {
                for (var i = 0; i < count; i++)
                {
                    Array.Fill(frame, (byte)(i * 4));
                    writer.WriteFrame(frame, width * 4, i * frameTicks);
                    writer.WriteSound(pcm, i * frameTicks, frameTicks);
                }
                writer.Finish(count * frameTicks);
            }
            AssertMp4Boxes(path);
            var (duration, frames) = Mp4Probe.Read(path);
            Assert.InRange(duration.TotalSeconds, 1.8, 2.3);
            Assert.Equal(count, frames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Gif_StopFinishesTheFile()
    {
        var (result, log) = await Record(VideoFormat.Gif, false);
        try
        {
            Assert.True(result.End == RecordingEnd.Stopped, $"{result.End}: {result.Message} | {string.Join(" | ", log)}");
            var bytes = File.ReadAllBytes(result.Path!);
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
            Assert.Equal(0x3B, bytes[^1]); // the trailer: the file was finished
        }
        finally
        {
            if (result.Path is { } p) File.Delete(p);
        }
    }

    private static void AssertMp4Boxes(string path)
    {
        var boxes = Mp4Probe.TopBoxes(path);
        Assert.Contains("ftyp", boxes);
        Assert.Contains("moov", boxes);
        Assert.Contains("mdat", boxes);
    }
}

/// <summary>Reads an MP4 back: its top-level boxes, and through Media Foundation's Source Reader its duration and video frame count.</summary>
internal static class Mp4Probe
{
    public static List<string> TopBoxes(string path)
    {
        var names = new List<string>();
        using var file = File.OpenRead(path);
        var header = new byte[16];
        while (file.Position + 8 <= file.Length)
        {
            var start = file.Position;
            file.ReadExactly(header, 0, 8);
            long size = (uint)(header[0] << 24 | header[1] << 16 | header[2] << 8 | header[3]);
            names.Add(System.Text.Encoding.ASCII.GetString(header, 4, 4));
            if (size == 1)
            {
                file.ReadExactly(header, 8, 8);
                size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
            }
            else if (size == 0) size = file.Length - start;
            if (size < 8) break;
            file.Position = start + size;
        }
        return names;
    }

    private const int MediaSource = unchecked((int)0xFFFFFFFF), AllStreams = unchecked((int)0xFFFFFFFE), FirstVideo = unchecked((int)0xFFFFFFFC);
    private const int EndOfStream = 0x2;
    private static readonly Guid PresentationDuration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    public static (TimeSpan Duration, int Frames) Read(string path)
    {
        Marshal.ThrowExceptionForHR(MFStartup(0x00020070, 0));
        try
        {
            Marshal.ThrowExceptionForHR(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out var reader));
            try
            {
                var variant = Marshal.AllocHGlobal(32);
                try
                {
                    var key = PresentationDuration;
                    Marshal.ThrowExceptionForHR(reader.GetPresentationAttribute(MediaSource, ref key, variant));
                    var duration = TimeSpan.FromTicks(Marshal.ReadInt64(variant, 8));
                    Marshal.ThrowExceptionForHR(reader.SetStreamSelection(AllStreams, 0));
                    Marshal.ThrowExceptionForHR(reader.SetStreamSelection(FirstVideo, 1));
                    var frames = 0;
                    while (true)
                    {
                        Marshal.ThrowExceptionForHR(reader.ReadSample(FirstVideo, 0, out _, out var flags, out _, out var sample));
                        if (sample != IntPtr.Zero)
                        {
                            frames++;
                            Marshal.Release(sample);
                        }
                        if ((flags & EndOfStream) != 0) break;
                    }
                    return (duration, frames);
                }
                finally
                {
                    Marshal.FreeHGlobal(variant);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        finally
        {
            MFShutdown();
        }
    }

    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int stream, out int selected);
        [PreserveSig] int SetStreamSelection(int stream, int selected);
        [PreserveSig] int GetNativeMediaType(int stream, int index, out IntPtr type);
        [PreserveSig] int GetCurrentMediaType(int stream, out IntPtr type);
        [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IntPtr type);
        [PreserveSig] int SetCurrentPosition(ref Guid format, IntPtr position);
        [PreserveSig] int ReadSample(int stream, int control, out int actualStream, out int flags, out long timestamp, out IntPtr sample);
        [PreserveSig] int Flush(int stream);
        [PreserveSig] int GetServiceForStream(int stream, ref Guid service, ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(int stream, ref Guid attribute, IntPtr value);
    }
}
