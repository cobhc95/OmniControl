using System.Runtime.InteropServices;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>The most a recording may grow: its length, its file size, and the free disk space it must leave.</summary>
public sealed record RecordingLimits(TimeSpan MaxLength, long MaxBytes, long MinFreeBytes);

/// <summary>What to record: the area (physical pixels, already even for MP4), the file, the format and its settings, sound and pointer.</summary>
public sealed record RecordingRequest(ScreenRect Area, string Path, VideoFormat Format, int FrameRate, VideoQuality Quality, bool Hardware,
    bool SystemSound, bool Microphone, PointerLook Pointer, RecordingLimits Limits);

/// <summary>Why a recording ended.</summary>
public enum RecordingEnd { Stopped, LengthLimit, SizeLimit, DiskFull, Failed }

/// <summary>How a recording ended: the file (null when nothing was kept), why, and a sentence when there is something to say.</summary>
public sealed record RecordingResult(string? Path, RecordingEnd End, string? Message);

/// <summary>
/// Records an area of the screen to an MP4 or a GIF on a thread of its own: a frame is copied every frame length of the recording's own
/// clock (<see cref="RecordingClock"/>, which stands still while paused), late frames are skipped rather than queued, and the sound
/// follows the same clock a little behind (<see cref="CoreTiming.RecordSoundLagMs"/>). The limits are checked every
/// <see cref="CoreTiming.RecordLimitCheckMs"/>. An MP4 with the hardware encoder keeps its frames on the graphics card (Desktop
/// Duplication into the encoder, <see cref="GpuFrames"/>) when it can, else, said once in the log, copies them with GDI
/// (<see cref="GdiFrames"/>), as a GIF always does (its encoding on a worker, <see cref="QueuedVideoSink"/>). Everything it opens
/// (screen copy, encoder, sound captures, timer resolution) is released when it ends, and a file that could not be finished is deleted.
/// </summary>
public sealed class ScreenRecorder
{
    private readonly RecordingRequest _request;
    private readonly Action<string> _log;
    private readonly int _frameRate;
    private readonly RecordingClock _clock = new();
    private readonly RecordingSound? _sound;
    private readonly object _signal = new();
    private readonly TaskCompletionSource<RecordingResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stop;
    private byte[] _pcm = Array.Empty<byte>();

    /// <summary>Finishes when the recording has ended and its file is complete (or deleted), whatever ended it.</summary>
    public Task<RecordingResult> Completion => _done.Task;

    /// <summary>The recording's length so far (pauses not counted).</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    public bool IsPaused => _clock.IsPaused;

    /// <summary>True when the file takes sound (MP4); a GIF has none.</summary>
    public bool TakesSound => _sound is not null;

    private ScreenRecorder(RecordingRequest request, Action<string> log)
    {
        _request = request;
        _log = log;
        _frameRate = VideoMath.FrameRateFor(request.Format, request.FrameRate);
        if (VideoMath.IsMp4(request.Format)) _sound = new RecordingSound();
    }

    /// <summary>
    /// Opens the file and starts recording. The result has the recorder, and a sentence when something was not as asked (sound that could
    /// not be opened, the other encoder used). Throws <see cref="InvalidOperationException"/> when the recording could not start.
    /// <paramref name="log"/> gets technical facts the person need not see (the graphics-card path not used, and why); any thread.
    /// </summary>
    public static async Task<(ScreenRecorder Recorder, string? Note)> StartAsync(RecordingRequest request, Action<string> log)
    {
        var recorder = new ScreenRecorder(request, log);
        var notes = new List<string>();
        if (recorder._sound is { } sound)
        {
            if (request.SystemSound && sound.SetSystem(true) is { } system) notes.Add(system);
            if (request.Microphone && sound.SetMicrophone(true) is { } mic) notes.Add(mic);
        }
        var thread = new Thread(recorder.Run) { IsBackground = true, Name = "Screen recording", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        var problem = await recorder._started.Task;
        if (recorder._done.Task.IsCompleted) throw new InvalidOperationException((await recorder._done.Task).Message);
        if (problem is not null) notes.Add(problem);
        return (recorder, notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    public void Pause()
    {
        _clock.Pause();
        Wake();
    }

    public void Resume()
    {
        _clock.Resume();
        Wake();
    }

    /// <summary>Turns recording what the speakers play on or off. Null when done, otherwise why not.</summary>
    public string? SetSystemSound(bool on) => _sound?.SetSystem(on);

    /// <summary>Turns recording the microphone on or off. Null when done, otherwise why not.</summary>
    public string? SetMicrophone(bool on) => _sound?.SetMicrophone(on);

    /// <summary>Asks the recording to end; <see cref="Completion"/> finishes once the file is complete.</summary>
    public void Stop()
    {
        _stop = true;
        Wake();
    }

    private void Wake()
    {
        lock (_signal) Monitor.PulseAll(_signal);
    }

    /// <summary>Waits up to <paramref name="ms"/> or until stopped, paused or resumed.</summary>
    private void Sleep(int ms)
    {
        lock (_signal)
        {
            if (!_stop) Monitor.Wait(_signal, ms);
        }
    }

    private void Run()
    {
        timeBeginPeriod(1); // frame times to the millisecond; given back below
        IVideoSink? sink = null;
        IFrameSource? frames = null;
        var end = RecordingEnd.Stopped;
        string? message = null;
        try
        {
            (sink, frames) = Open(out var note);
            _started.TrySetResult(note);
            end = Record(frames, sink);
            sink.Finish(_clock.Ticks);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            end = RecordingEnd.Failed;
            message = ex.Message;
        }
        finally
        {
            sink?.Dispose(); // before the frames: an MP4 on the graphics card uses their device until it is released
            frames?.Dispose();
            _sound?.Dispose();
            timeEndPeriod(1);
        }
        var path = end == RecordingEnd.Failed ? Delete(_request.Path) : _request.Path;
        _done.TrySetResult(new RecordingResult(path, end, message ?? EndMessage(end)));
        _started.TrySetResult(null); // after the result, so a start that failed is seen as failed
    }

    /// <summary>Opens the file and the way its frames are copied: on the graphics card for an MP4 with the hardware encoder when possible, else with GDI.</summary>
    private (IVideoSink Sink, IFrameSource Frames) Open(out string? note)
    {
        note = null;
        var area = _request.Area;
        if (_request.Format == VideoFormat.Gif)
            return WithGdi(() => new QueuedVideoSink(new GifWriter(_request.Path, area.Width, area.Height, _request.Quality), area.Width, area.Height, "GIF encoding"));
        var bitrate = VideoMath.Bitrate(area.Width, area.Height, _frameRate, _request.Quality, _request.Format);
        if (_request.Hardware && OpenOnGpu(bitrate) is { } onGpu) return onGpu;
        string? encoderNote = null;
        var opened = WithGdi(() => Mp4Writer.Open(_request.Path, area.Width, area.Height, _frameRate, _request.Format, bitrate, _request.Hardware, out encoderNote));
        note = encoderNote;
        return opened;
    }

    private (IVideoSink, IFrameSource) WithGdi(Func<IVideoSink> openSink)
    {
        var grabber = new FrameGrabber(_request.Area, _request.Pointer);
        try
        {
            var sink = openSink();
            return (sink, new GdiFrames(grabber, sink));
        }
        catch
        {
            grabber.Dispose();
            throw;
        }
    }

    /// <summary>Desktop Duplication into an MP4 writer on the same graphics card; null (said in the log) when either is not possible here.</summary>
    private (IVideoSink, IFrameSource)? OpenOnGpu(int bitrate)
    {
        var area = _request.Area;
        var capture = DesktopDuplication.TryOpen(area, _request.Pointer, _log, out var why);
        if (capture is null)
        {
            _log($"Screen recording: frames are copied with GDI because the graphics-card copy is not available ({why}).");
            return null;
        }
        try
        {
            var writer = Mp4Writer.OpenOnGpu(_request.Path, area.Width, area.Height, _frameRate, _request.Format, bitrate, capture);
            return (writer, new GpuFrames(capture, writer));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            capture.Dispose();
            _log($"Screen recording: frames are copied with GDI because the encoder cannot take frames from the graphics card ({ex.Message}).");
            return null;
        }
    }

    /// <summary>The loop: a frame when one is due, the sound up to the clock less the lag, the limits now and then; until stopped or a limit.</summary>
    private RecordingEnd Record(IFrameSource frames, IVideoSink sink)
    {
        var nextFrame = 0L;
        var soundWritten = 0L;
        var nextCheck = 0L;
        var checkGap = TimeSpan.FromMilliseconds(CoreTiming.RecordLimitCheckMs).Ticks;
        var wasPaused = false;
        while (!_stop)
        {
            if (_clock.IsPaused)
            {
                wasPaused = true;
                Sleep(CoreTiming.RecordLimitCheckMs);
                continue;
            }
            if (wasPaused)
            {
                wasPaused = false;
                _sound?.Clear(); // nothing heard during the pause goes in
            }
            var now = _clock.Ticks;
            var frame = VideoMath.FrameAt(now, _frameRate);
            if (frame >= nextFrame)
            {
                frames.Write(VideoMath.FrameStart(frame, _frameRate));
                nextFrame = frame + 1;
            }
            if (sink is ISoundSink soundSink) soundWritten = WriteSound(soundSink, soundWritten, now);
            if (now >= nextCheck)
            {
                nextCheck = now + checkGap;
                if (OverLimit(now, sink) is { } limit) return limit;
            }
            var wait = (VideoMath.FrameStart(nextFrame, _frameRate) - _clock.Ticks) / TimeSpan.TicksPerMillisecond;
            if (wait > 0) Sleep((int)wait);
        }
        return RecordingEnd.Stopped;
    }

    /// <summary>Writes the sound from where it stopped up to the clock less the lag, in pieces of at least the chunk length. The frames written so far.</summary>
    private long WriteSound(ISoundSink sink, long written, long now)
    {
        var lag = TimeSpan.FromMilliseconds(CoreTiming.RecordSoundLagMs).Ticks;
        var due = Math.Max(0, now - lag) * RecordingSound.SampleRate / VideoMath.TicksPerSecond;
        var count = due - written;
        if (count < RecordingSound.SampleRate * CoreTiming.RecordSoundChunkMs / 1000) return written;
        var length = (int)count * RecordingSound.BytesPerFrame;
        if (_pcm.Length < length) _pcm = new byte[length];
        var pcm = _pcm.AsSpan(0, length);
        _sound!.Read(pcm);
        var at = written * VideoMath.TicksPerSecond / RecordingSound.SampleRate;
        sink.WriteSound(pcm, at, count * VideoMath.TicksPerSecond / RecordingSound.SampleRate);
        return due;
    }

    private RecordingEnd? OverLimit(long now, IVideoSink sink)
    {
        var limits = _request.Limits;
        if (TimeSpan.FromTicks(now) >= limits.MaxLength) return RecordingEnd.LengthLimit;
        if (sink.Length >= limits.MaxBytes) return RecordingEnd.SizeLimit;
        var root = Path.GetPathRoot(Path.GetFullPath(_request.Path));
        if (string.IsNullOrEmpty(root)) return null;
        try
        {
            return new DriveInfo(root).AvailableFreeSpace < limits.MinFreeBytes ? RecordingEnd.DiskFull : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // a network folder whose free space Windows does not report: only the size limit applies
        }
    }

    private string? EndMessage(RecordingEnd end) => end switch
    {
        RecordingEnd.LengthLimit => $"The recording stopped at its {Words(_request.Limits.MaxLength)} limit.",
        RecordingEnd.SizeLimit => "The recording stopped at its file size limit.",
        RecordingEnd.DiskFull => "The recording stopped because the disk is nearly full.",
        _ => null,
    };

    private static string Words(TimeSpan length) =>
        length.TotalHours >= 1 ? $"{length.TotalHours:0.#} hour" : $"{length.TotalMinutes:0} minute";

    /// <summary>Removes a file that could not be finished. Null (no file is kept).</summary>
    private static string? Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the unfinished file stays; the failure message already says the recording did not work
        }
        return null;
    }
}
