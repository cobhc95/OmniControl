using System.Runtime.InteropServices;
using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>Where one try at recording is.</summary>
internal enum AttemptState { Opening, Recording, Finishing, Done, Failed }

/// <summary>
/// One try at recording in one way (<see cref="RecordingStep"/>), on a thread of its own (MTA, so no encoder or capture it makes can end
/// up on the UI thread's apartment), into a part file of its own next to the final file, so a try that hangs inside Windows can be left
/// behind while the next one records. It opens the screen copy and the file, then writes a frame on the clock's grid (late frames are
/// skipped), the sound a little behind (<see cref="CoreTiming.RecordSoundLagMs"/>) and checks the limits, until stopped; then it finishes the
/// file. It never decides what happens next: it only says how far it got (<see cref="State"/>, how long the call in progress has taken,
/// the frames encoded), and <see cref="ScreenRecorder"/>'s watchdog acts on that. A try that is left behind (<see cref="Abandon"/>) touches
/// nothing shared again and deletes its part file whenever Windows lets its thread go. Every step is logged.
/// </summary>
internal sealed class RecordingAttempt
{
    private readonly RecordingRequest _request;
    private readonly RecordingLog _log;
    private readonly RecordingSound? _sound;
    private readonly IReadOnlyList<GraphicsAdapter> _adapters;
    private readonly RecordingFaults? _faults;
    private readonly int _frameRate;
    private readonly object _signal = new();
    private readonly HashSet<string> _logged = new();
    private volatile AttemptState _state = AttemptState.Opening;
    private long _stateSince = Environment.TickCount64, _callSince;
    private long _framesDone, _handed, _skipped;
    private volatile string? _call;
    private volatile bool _stop, _abandoned, _pauseWanted;
    private volatile RecordingClock? _clock;
    private byte[] _pcm = Array.Empty<byte>();

    public RecordingStep Step { get; }

    /// <summary>The file this try writes; moved over the final file once it is complete.</summary>
    public string PartPath { get; }

    public AttemptState State => _state;

    /// <summary>Why the recording ended, once <see cref="State"/> is <see cref="AttemptState.Done"/>.</summary>
    public RecordingEnd End { get; private set; } = RecordingEnd.Stopped;

    /// <summary>Why it failed (one sentence), once <see cref="State"/> is <see cref="AttemptState.Failed"/>.</summary>
    public string? Problem { get; private set; }

    /// <summary>The failure is the disk's or the folder's, so no other way of recording can do better.</summary>
    public bool Fatal { get; private set; }

    /// <summary>Something not as asked that the person should hear (H.264 used for HEVC).</summary>
    public string? Note { get; private set; }

    /// <summary>How long the current state has lasted.</summary>
    public long StateMs => Environment.TickCount64 - Interlocked.Read(ref _stateSince);

    /// <summary>How long the call into the encoder or the screen copy in progress has taken (0 when none is).</summary>
    public long CallMs
    {
        get
        {
            var since = Interlocked.Read(ref _callSince);
            return since == 0 ? 0 : Environment.TickCount64 - since;
        }
    }

    public string? CallName => _call;

    public long FramesDone => Interlocked.Read(ref _framesDone);

    /// <summary>The try's clock from the moment it records (null while it opens).</summary>
    public RecordingClock? Clock => _clock;

    public RecordingAttempt(RecordingStep step, RecordingRequest request, string partPath, int frameRate, RecordingSound? sound,
        IReadOnlyList<GraphicsAdapter> adapters, RecordingLog log, RecordingFaults? faults)
    {
        Step = step;
        _request = request;
        PartPath = partPath;
        _frameRate = frameRate;
        _sound = sound;
        _adapters = adapters;
        _log = log;
        _faults = faults;
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = $"Screen recording {Step}", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>Ends the recording; the file is finished.</summary>
    public void Stop()
    {
        _stop = true;
        Wake();
    }

    /// <summary>Leaves this try behind: it stops as soon as its thread is free, touches nothing shared and deletes its part file.</summary>
    public void Abandon()
    {
        _abandoned = true;
        _stop = true;
        Wake();
    }

    public void Pause()
    {
        _pauseWanted = true;
        _clock?.Pause();
        Wake();
    }

    public void Resume()
    {
        _pauseWanted = false;
        _clock?.Resume();
        Wake();
    }

    private void Wake()
    {
        lock (_signal) Monitor.PulseAll(_signal);
    }

    private void Sleep(int ms)
    {
        lock (_signal)
        {
            if (!_stop) Monitor.Wait(_signal, ms);
        }
    }

    private void SetState(AttemptState state)
    {
        Interlocked.Exchange(ref _stateSince, Environment.TickCount64);
        _state = state;
    }

    private void Run()
    {
        IVideoSink? sink = null;
        IFrameSource? frames = null;
        var finished = false;
        try
        {
            try
            {
                (sink, frames) = Open();
                if (!_abandoned)
                {
                    var clock = new RecordingClock(); // the video starts now: an encoder can take a second or more to open
                    if (_pauseWanted) clock.Pause();
                    _clock = clock;
                    _sound?.Clear();
                    SetState(AttemptState.Recording);
                    End = Record(frames, sink, clock);
                    if (!_abandoned)
                    {
                        SetState(AttemptState.Finishing);
                        var end = clock.Ticks;
                        Fault(RecordingFault.HangFinish);
                        _log.Step("finish the file (Sink Writer Finalize / GIF trailer)", () => sink.Finish(end));
                        finished = true;
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                           or DllNotFoundException or EntryPointNotFoundException)
            {
                Problem = ex.Message;
                Fatal = ex is IOException or UnauthorizedAccessException || ex.HResult == DiskFull;
                _log.Line($"{Step} failed: {RecordingLog.Describe(ex)}");
            }
            finally
            {
                if (sink is not null) _log.Step("close the file", sink.Dispose); // before the screen copy: an MP4 on the graphics card uses its device until released
            }
            SetState(finished && !_abandoned ? AttemptState.Done : AttemptState.Failed); // the file is complete: releasing the screen copy below never holds Stop up
        }
        finally
        {
            if (frames is not null) _log.Step("release the screen copy", frames.Dispose);
            if (_state != AttemptState.Done) Delete(PartPath, _log);
        }
    }

    private const int DiskFull = unchecked((int)0x80070070);

    /// <summary>Opens this way's file and screen copy; throws when it cannot.</summary>
    private (IVideoSink Sink, IFrameSource Frames) Open()
    {
        _log.Line($"trying {Step}: {RecordingSteps.Words(Step)}, file {Path.GetFileName(PartPath)}");
        Fault(RecordingFault.HangOpen);
        if (Has(RecordingFault.Fail))
            throw new InvalidOperationException("made to fail by a test");
        var area = _request.Area;
        if (Step == RecordingStep.Gif)
            return WithGdi(() => new QueuedVideoSink(new GifWriter(PartPath, area.Width, area.Height, _request.Quality), area.Width, area.Height, "GIF encoding"));
        var bitrate = VideoMath.Bitrate(area.Width, area.Height, _frameRate, _request.Quality, _request.Format);
        return Step switch
        {
            RecordingStep.GraphicsCard => OnGpu(bitrate),
            RecordingStep.OtherEncoder => WithGdi(() => OtherEncoder(bitrate)),
            _ => WithGdi(() => Software(bitrate)),
        };
    }

    /// <summary>Desktop Duplication into the encoder, all on the adapter that drives the monitor; refused when the encoder Windows chose says it is on another one.</summary>
    private (IVideoSink, IFrameSource) OnGpu(int bitrate)
    {
        var area = _request.Area;
        string? why = null;
        var capture = _log.Step("Desktop Duplication open", () => DesktopDuplication.TryOpen(area, _request.Pointer, _log, out why))
                      ?? throw new InvalidOperationException($"the graphics-card screen copy is not available ({why})");
        try
        {
            var writer = Mp4Writer.OpenOnGpu(PartPath, area.Width, area.Height, _frameRate, _request.Format, bitrate, capture, _log);
            if (writer.Encoder?.Luid is { } luid && luid != capture.AdapterLuid)
            {
                writer.Dispose();
                throw new InvalidOperationException("the encoder Windows chose is on another graphics adapter than the screen copy");
            }
            return (writer, new GpuFrames(capture, writer));
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A hardware encoder fed from memory: on another adapter that has one when there is such an adapter (its device goes to the Sink
    /// Writer), else on whichever Windows picks.
    /// </summary>
    private IVideoSink OtherEncoder(int bitrate)
    {
        var area = _request.Area;
        var subtype = _request.Format == VideoFormat.Mp4Hevc ? Hevc : H264;
        var owner = _adapters.FirstOrDefault(a => a.Shows(area));
        var other = _faults?.Adapter ?? _adapters.FirstOrDefault(a => !a.Software && a != owner && GraphicsAdapters.HardwareEncoders(a.Luid, subtype).Count > 0)?.Luid;
        if (other is not { } luid)
        {
            _log.Line("no other adapter has a hardware encoder; Windows picks the encoder");
            return Mp4Writer.Open(PartPath, area.Width, area.Height, _frameRate, _request.Format, bitrate, true, _log);
        }
        var device = _log.Step($"Direct3D device on adapter LUID {(uint)(luid >> 32):X8}:{(uint)luid:X8}", () => GraphicsAdapters.CreateDevice(luid));
        try
        {
            return Mp4Writer.OpenWithDevice(PartPath, area.Width, area.Height, _frameRate, _request.Format, bitrate, device, _log);
        }
        finally
        {
            Release(device); // the device manager keeps its own reference
        }
    }

    /// <summary>Windows' software encoder; HEVC (which has none on many PCs) becomes H.264, said in <see cref="Note"/>.</summary>
    private IVideoSink Software(int bitrate)
    {
        var area = _request.Area;
        try
        {
            return Mp4Writer.Open(PartPath, area.Width, area.Height, _frameRate, _request.Format, bitrate, false, _log);
        }
        catch (COMException ex) when (_request.Format == VideoFormat.Mp4Hevc)
        {
            _log.Line($"software HEVC refused ({RecordingLog.Describe(ex)}); H.264 instead");
            Note = "HEVC could not be encoded here, so the video is H.264.";
            var h264 = VideoMath.Bitrate(area.Width, area.Height, _frameRate, _request.Quality, VideoFormat.Mp4H264);
            return Mp4Writer.Open(PartPath, area.Width, area.Height, _frameRate, VideoFormat.Mp4H264, h264, false, _log);
        }
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

    /// <summary>The loop: a frame when one is due, the sound up to the clock less the lag, the counts and limits now and then; until stopped or a limit.</summary>
    private RecordingEnd Record(IFrameSource frames, IVideoSink sink, RecordingClock clock)
    {
        long nextFrame = 0, soundWritten = 0, nextCheck = 0, nextStats = 0, nextLog = 0;
        var checkGap = TimeSpan.FromMilliseconds(CoreTiming.RecordLimitCheckMs).Ticks;
        var statsGap = TimeSpan.FromMilliseconds(CoreTiming.RecordStatsMs).Ticks;
        var logGap = TimeSpan.FromMilliseconds(CoreTiming.RecordLogFramesMs).Ticks;
        var silent = Has(RecordingFault.Silent);
        var wasPaused = false;
        while (!_stop)
        {
            if (clock.IsPaused)
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
            var now = clock.Ticks;
            var frame = VideoMath.FrameAt(now, _frameRate);
            if (frame >= nextFrame)
            {
                var at = VideoMath.FrameStart(frame, _frameRate);
                if (Call("frame write", () => frames.Write(at))) _handed++;
                else _skipped++;
                nextFrame = frame + 1;
            }
            if (sink is ISoundSink soundSink && !_abandoned) soundWritten = Call("sound write", () => WriteSound(soundSink, soundWritten, now));
            if (now >= nextStats)
            {
                nextStats = now + statsGap;
                var done = silent ? 0 : Call("encoder counts", () => sink.FramesDone);
                if (done > 0 && Interlocked.Exchange(ref _framesDone, done) == 0) _log.Line($"first frame encoded by {TimeSpan.FromTicks(now).TotalMilliseconds:0} ms of recording");
            }
            if (now >= nextLog)
            {
                nextLog = now + logGap;
                _log.Line($"at {TimeSpan.FromTicks(now).TotalSeconds:0.0} s: frames handed {_handed}, skipped {_skipped}, encoded {FramesDone}{(sink is Mp4Writer mp4 ? $" ({Call("encoder counts", mp4.Counts)})" : "")}, file {sink.Length / 1024} KB");
            }
            if (now >= nextCheck)
            {
                nextCheck = now + checkGap;
                if (OverLimit(now, sink) is { } limit)
                {
                    _log.Line($"limit reached: {limit}");
                    return limit;
                }
            }
            var wait = (VideoMath.FrameStart(nextFrame, _frameRate) - clock.Ticks) / TimeSpan.TicksPerMillisecond;
            if (wait > 0) Sleep((int)wait);
        }
        _log.Line($"recording loop ended: frames handed {_handed}, skipped {_skipped}, encoded {FramesDone}");
        return RecordingEnd.Stopped;
    }

    /// <summary>A call into the encoder or the screen copy, timed for the watchdog; its first run is logged so a first call that never returns shows.</summary>
    private T Call<T>(string what, Func<T> call)
    {
        var first = _logged.Add(what);
        if (first) _log.Line($"first {what}...");
        _call = what;
        Interlocked.Exchange(ref _callSince, Environment.TickCount64);
        try
        {
            if (first && what == "frame write") Fault(RecordingFault.HangWrite);
            var result = call();
            if (first) _log.Line($"first {what}: done in {CallMs} ms");
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _callSince, 0);
        }
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
        var root = Path.GetPathRoot(Path.GetFullPath(PartPath));
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

    /// <summary>A test's hang: blocks here, as a stuck driver call would, until the test lets go.</summary>
    private void Fault(RecordingFault fault)
    {
        if (!Has(fault)) return;
        _log.Line($"test fault: {fault}");
        _faults!.Release.Wait();
    }

    private bool Has(RecordingFault fault) => _faults is not null && _faults.At.TryGetValue(Step, out var set) && set == fault;

    /// <summary>Removes a file that is not kept (logged; a file Windows still holds stays).</summary>
    public static void Delete(string path, RecordingLog log)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Line($"{Path.GetFileName(path)} could not be deleted: {ex.Message}");
        }
    }
}
