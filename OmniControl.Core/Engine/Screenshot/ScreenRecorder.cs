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
/// <see cref="CoreTiming.RecordLimitCheckMs"/>. Everything it opens (screen copy, encoder, sound captures, timer resolution) is released
/// when it ends, and a file that could not be finished is deleted.
/// </summary>
public sealed class ScreenRecorder
{
    private readonly RecordingRequest _request;
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

    private ScreenRecorder(RecordingRequest request)
    {
        _request = request;
        _frameRate = VideoMath.FrameRateFor(request.Format, request.FrameRate);
        if (VideoMath.IsMp4(request.Format)) _sound = new RecordingSound();
    }

    /// <summary>
    /// Opens the file and starts recording. The result has the recorder, and a sentence when something was not as asked (sound that could
    /// not be opened, the other encoder used). Throws <see cref="InvalidOperationException"/> when the recording could not start.
    /// </summary>
    public static async Task<(ScreenRecorder Recorder, string? Note)> StartAsync(RecordingRequest request)
    {
        var recorder = new ScreenRecorder(request);
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
        var end = RecordingEnd.Stopped;
        string? message = null;
        try
        {
            using var grabber = new FrameGrabber(_request.Area, _request.Pointer);
            sink = Open(out var note);
            _started.TrySetResult(note);
            end = Record(grabber, sink);
            sink.Finish(_clock.Ticks);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            end = RecordingEnd.Failed;
            message = ex.Message;
        }
        finally
        {
            sink?.Dispose();
            _sound?.Dispose();
            timeEndPeriod(1);
        }
        var path = end == RecordingEnd.Failed ? Delete(_request.Path) : _request.Path;
        _done.TrySetResult(new RecordingResult(path, end, message ?? EndMessage(end)));
        _started.TrySetResult(null); // after the result, so a start that failed is seen as failed
    }

    private IVideoSink Open(out string? note)
    {
        note = null;
        var area = _request.Area;
        if (_request.Format == VideoFormat.Gif) return new GifWriter(_request.Path, area.Width, area.Height, _request.Quality);
        var bitrate = VideoMath.Bitrate(area.Width, area.Height, _frameRate, _request.Quality, _request.Format);
        return Mp4Writer.Open(_request.Path, area.Width, area.Height, _frameRate, _request.Format, bitrate, _request.Hardware, out note);
    }

    /// <summary>The loop: a frame when one is due, the sound up to the clock less the lag, the limits now and then; until stopped or a limit.</summary>
    private RecordingEnd Record(FrameGrabber grabber, IVideoSink sink)
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
                sink.WriteFrame(grabber.Grab(), grabber.Stride, VideoMath.FrameStart(frame, _frameRate));
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
