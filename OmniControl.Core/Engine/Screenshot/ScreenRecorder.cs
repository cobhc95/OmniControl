using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>The most a recording may grow: its length, its file size, and the free disk space it must leave.</summary>
public sealed record RecordingLimits(TimeSpan MaxLength, long MaxBytes, long MinFreeBytes);

/// <summary>
/// What to record: the area (physical pixels, already even for MP4), the file, the format and its settings, sound and pointer, and the
/// way remembered from earlier recordings on this PC (<see cref="RecordingRoute"/>; null = start at the top).
/// </summary>
public sealed record RecordingRequest(ScreenRect Area, string Path, VideoFormat Format, int FrameRate, VideoQuality Quality, bool Hardware,
    bool SystemSound, bool Microphone, PointerLook Pointer, RecordingLimits Limits, RecordingRoute? Route = null);

/// <summary>Why a recording ended.</summary>
public enum RecordingEnd { Stopped, LengthLimit, SizeLimit, DiskFull, Failed, Cancelled }

/// <summary>
/// How a recording ended: the file (null when nothing was kept), why, a sentence when there is something to say, the way to remember for
/// the next recording on this PC (the caller stores it when it differs from what it passed), and the recording's log lines ("Copy details").
/// </summary>
public sealed record RecordingResult(string? Path, RecordingEnd End, string? Message, RecordingRoute? Route = null, string? Details = null);

/// <summary>A recording that could not start: the sentence for the person, and the recording's log lines for "Copy details".</summary>
public sealed class RecordingFailedException(string message, string details) : InvalidOperationException(message)
{
    public string Details { get; } = details;
}

/// <summary>
/// Records an area of the screen to an MP4 or a GIF, and never hangs or blocks its caller: a watchdog thread (MTA) runs the recording as a
/// chain of ways (<see cref="RecordingStep"/>: the graphics card's encoder fed by Desktop Duplication on the adapter that drives the monitor;
/// a graphics card's encoder, another adapter's when there is one, fed by GDI; the software encoder), each a <see cref="RecordingAttempt"/>
/// on its own thread and part file. A way that does not open within <see cref="CoreTiming.RecordOpenWaitMs"/>, encodes no frame within
/// <see cref="CoreTiming.RecordFirstFrameWaitMs"/>, has a call that does not return within <see cref="CoreTiming.RecordStuckMs"/> or fails
/// is left behind and the next one records (said in the log and to the person); a Stop whose file is not finished within
/// <see cref="CoreTiming.RecordFinishWaitMs"/> ends as failed, and the next recording starts at the next way. The way that worked is
/// handed back to be remembered for this PC's drivers. Every method here only sets a flag or starts a task, so the UI thread never waits
/// on Windows' encoder, screen copy or sound. Everything it opens is released when it ends, and a file that could not be finished is deleted.
/// </summary>
public sealed class ScreenRecorder
{
    private readonly RecordingRequest _request;
    private readonly RecordingLog _log;
    private readonly Action<string>? _notice;
    private readonly RecordingWaits _waits;
    private readonly RecordingFaults? _faults;
    private readonly int _frameRate;
    private readonly RecordingSound? _sound;
    private readonly object _signal = new();
    private readonly List<string> _notes = new();
    private readonly TaskCompletionSource<RecordingResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stop, _abort, _paused;
    private volatile RecordingAttempt? _attempt;

    /// <summary>Finishes when the recording has ended and its file is complete (or deleted), whatever ended it.</summary>
    public Task<RecordingResult> Completion => _done.Task;

    /// <summary>The recording's length so far (pauses not counted; from the start of the way that records now).</summary>
    public TimeSpan Elapsed => _attempt?.Clock?.Elapsed ?? TimeSpan.Zero;

    public bool IsPaused => _paused;

    /// <summary>True when the file takes sound (MP4); a GIF has none.</summary>
    public bool TakesSound => _sound is not null;

    private ScreenRecorder(RecordingRequest request, Action<string> log, Action<string>? notice, RecordingWaits waits, RecordingFaults? faults)
    {
        _request = request;
        _log = new RecordingLog(log);
        _notice = notice;
        _waits = waits;
        _faults = faults;
        _frameRate = VideoMath.FrameRateFor(request.Format, request.FrameRate);
        if (VideoMath.IsMp4(request.Format)) _sound = new RecordingSound();
    }

    /// <summary>
    /// Starts recording. The result has the recorder, and a sentence when something was not as asked (sound that could not be opened, another
    /// way used). Throws <see cref="RecordingFailedException"/> when no way could start. <paramref name="log"/> gets the recording's diagnostic
    /// lines (<see cref="RecordingLog"/>) and <paramref name="notice"/> a sentence when the way changes while recording; both on any thread.
    /// </summary>
    public static Task<(ScreenRecorder Recorder, string? Note)> StartAsync(RecordingRequest request, Action<string> log, Action<string>? notice = null) =>
        StartAsync(request, log, notice, RecordingWaits.Default, null);

    internal static async Task<(ScreenRecorder Recorder, string? Note)> StartAsync(RecordingRequest request, Action<string> log, Action<string>? notice,
        RecordingWaits waits, RecordingFaults? faults)
    {
        var recorder = new ScreenRecorder(request, log, notice, waits, faults);
        var thread = new Thread(recorder.Watch) { IsBackground = true, Name = "Screen recording watchdog" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        string? note;
        try
        {
            note = await recorder._started.Task.WaitAsync(TimeSpan.FromMilliseconds(CoreTiming.RecordStartWaitMs)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            recorder.Abort();
            throw new RecordingFailedException("it did not start in time; the details are in the log.", recorder._log.Text);
        }
        if (recorder._done.Task.IsCompleted)
        {
            var result = await recorder._done.Task.ConfigureAwait(false);
            throw new RecordingFailedException(result.Message ?? "it did not start.", result.Details ?? recorder._log.Text);
        }
        return (recorder, note);
    }

    public void Pause()
    {
        _paused = true;
        _attempt?.Pause();
        Wake();
    }

    public void Resume()
    {
        _paused = false;
        _attempt?.Resume();
        Wake();
    }

    /// <summary>Turns recording what the speakers play on or off, off the caller's thread. Null when done, otherwise why not.</summary>
    public Task<string?> SetSystemSoundAsync(bool on) => _sound is { } sound ? Task.Run(() => sound.SetSystem(on)) : Task.FromResult<string?>(null);

    /// <summary>Turns recording the microphone on or off, off the caller's thread. Null when done, otherwise why not.</summary>
    public Task<string?> SetMicrophoneAsync(bool on) => _sound is { } sound ? Task.Run(() => sound.SetMicrophone(on)) : Task.FromResult<string?>(null);

    /// <summary>Asks the recording to end; <see cref="Completion"/> finishes once the file is complete (or the watchdog gives up on it).</summary>
    public void Stop()
    {
        _stop = true;
        Wake();
    }

    /// <summary>
    /// Ends the recording at once and keeps nothing: <see cref="Completion"/> finishes now (no file), even when a recording thread is stuck
    /// in the encoder; that thread releases what it holds and deletes its part file whenever it gets free.
    /// </summary>
    public void Abort()
    {
        _abort = true;
        _stop = true;
        Wake();
        _done.TrySetResult(new RecordingResult(null, RecordingEnd.Cancelled, null, _request.Route, _log.Text));
    }

    private void Wake()
    {
        lock (_signal) Monitor.PulseAll(_signal);
    }

    private void Sleep(int ms)
    {
        lock (_signal) Monitor.Wait(_signal, ms);
    }

    /// <summary>The watchdog: opens the sound, lists the adapters, then runs the chain of ways until one ends the recording.</summary>
    private void Watch()
    {
        timeBeginPeriod(1); // frame times to the millisecond; given back below
        try
        {
            var area = _request.Area;
            _log.Line($"start: {VideoMath.Name(_request.Format)} {area.Width}x{area.Height} at {area.X},{area.Y}, {_request.FrameRate} fps asked, {_frameRate} used, " +
                      $"quality {_request.Quality}, hardware encoder {(_request.Hardware ? "on" : "off")}, system sound {(_request.SystemSound ? "on" : "off")}, " +
                      $"microphone {(_request.Microphone ? "on" : "off")}; Windows {Environment.OSVersion.Version}, {Thread.CurrentThread.GetApartmentState()}");
            OpenSound();
            var adapters = GraphicsAdapters.List(_log);
            var key = GraphicsAdapters.MachineKey(adapters);
            var chain = RecordingSteps.Chain(_request.Format, _request.Hardware);
            if (chain.Count > 1) LogEncoders(adapters);
            RunChain(chain, RecordingSteps.StartIndex(chain, _request.Route, key, _log), adapters, key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Line($"the watchdog failed: {RecordingLog.Describe(ex)}"); // a bug must not leave the caller waiting
            End(new RecordingResult(null, RecordingEnd.Failed, "the recording failed; the details are in the log.", _request.Route, _log.Text));
        }
        finally
        {
            if (_sound is { } sound) _log.Step("sound stop", sound.Dispose);
            timeEndPeriod(1);
            _log.Line("recording over");
        }
    }

    /// <summary>Which hardware encoders each real adapter has for the format (Windows' list; the encoder itself does not say its adapter).</summary>
    private void LogEncoders(IReadOnlyList<GraphicsAdapter> adapters)
    {
        var subtype = _request.Format == VideoFormat.Mp4Hevc ? Native.MediaFoundationNative.Hevc : Native.MediaFoundationNative.H264;
        foreach (var adapter in adapters.Where(a => !a.Software))
        {
            var names = string.Join(", ", GraphicsAdapters.HardwareEncoders(adapter.Luid, subtype));
            _log.Line($"hardware encoders on adapter {adapter.Index}: {(names.Length > 0 ? names : "none")}");
        }
    }

    private void OpenSound()
    {
        if (_sound is not { } sound) return;
        if (_request.SystemSound && _log.Step("system sound open", () => sound.SetSystem(true)) is { } system) _notes.Add(system);
        if (_request.Microphone && _log.Step("microphone open", () => sound.SetMicrophone(true)) is { } mic) _notes.Add(mic);
    }

    /// <summary>Tries each way from <paramref name="index"/> on until one ends the recording, or none is left.</summary>
    private void RunChain(IReadOnlyList<RecordingStep> chain, int index, IReadOnlyList<GraphicsAdapter> adapters, string key)
    {
        var tries = 0;
        for (; index < chain.Count; index++)
        {
            var step = chain[index];
            var attempt = new RecordingAttempt(step, _request, $"{_request.Path}.part{++tries}", _frameRate, _sound, adapters, _log, _faults);
            _attempt = attempt;
            if (_paused) attempt.Pause();
            attempt.Start();
            var (outcome, why) = Follow(attempt);
            switch (outcome)
            {
                case Outcome.Done:
                    Keep(attempt, chain, key);
                    return;
                case Outcome.Cancelled:
                    return;
                case Outcome.Fatal:
                    End(Failed(why));
                    return;
                case Outcome.FinishHung:
                    End(new RecordingResult(null, RecordingEnd.Failed, "the encoder did not finish the file in time, so it could not be saved; the next recording uses another way.",
                        RecordingSteps.AfterHang(chain, step, key, _request.Route), _log.Text));
                    return;
            }
            _log.Line($"{step} given up: {why}");
            if (_stop)
            {
                End(Failed($"it could not be finished ({why})."));
                return;
            }
            if (index + 1 < chain.Count) Tell($"{Capital(RecordingSteps.Words(step))} did not work, so the recording restarted with {RecordingSteps.Words(chain[index + 1])}.");
        }
        End(Failed("no way of recording worked on this PC; the details are in the log."));
    }

    private enum Outcome { Done, Cancelled, Fatal, FinishHung, MoveOn }

    /// <summary>Watches one way until it ends the recording or must be left behind (it is then abandoned here).</summary>
    private (Outcome, string?) Follow(RecordingAttempt attempt)
    {
        var stopSent = false;
        var pausedSeen = false;
        var recordingSeen = false;
        while (true)
        {
            if (_abort)
            {
                _log.Line("Cancel asked: nothing is kept");
                attempt.Abandon();
                return (Outcome.Cancelled, null);
            }
            if (_stop && !stopSent)
            {
                stopSent = true;
                _log.Line("Stop asked");
                attempt.Stop();
            }
            if (_paused != pausedSeen)
            {
                pausedSeen = _paused;
                _log.Line(pausedSeen ? "paused" : "resumed");
            }
            if (!recordingSeen && attempt.State == AttemptState.Recording)
            {
                recordingSeen = true;
                if (attempt.Note is { } note) Tell(note);
                _started.TrySetResult(_notes.Count > 0 ? string.Join(" ", _notes) : null); // the first way that records ends the start
            }
            if (Check(attempt) is { } verdict)
            {
                if (verdict.Item1 is Outcome.MoveOn or Outcome.FinishHung) attempt.Abandon();
                return verdict;
            }
            Sleep(CoreTiming.RecordWatchMs);
        }
    }

    /// <summary>What the watchdog makes of a way now: null while all is well.</summary>
    private (Outcome, string?)? Check(RecordingAttempt attempt)
    {
        switch (attempt.State)
        {
            case AttemptState.Opening:
                return attempt.StateMs > _waits.OpenMs ? (Outcome.MoveOn, $"it did not open within {_waits.OpenMs / 1000.0:0.#} s") : null;
            case AttemptState.Recording:
                if (attempt.CallMs > _waits.StuckMs) return (Outcome.MoveOn, $"its {attempt.CallName} did not return within {_waits.StuckMs / 1000.0:0.#} s");
                if (attempt.FramesDone == 0 && attempt.Clock is { } clock && clock.Elapsed.TotalMilliseconds > _waits.FirstFrameMs)
                    return (Outcome.MoveOn, $"no frame was encoded within {_waits.FirstFrameMs / 1000.0:0.#} s");
                return null;
            case AttemptState.Finishing:
                return attempt.StateMs > _waits.FinishMs ? (Outcome.FinishHung, $"finishing took over {_waits.FinishMs / 1000.0:0.#} s") : null;
            case AttemptState.Done:
                return (Outcome.Done, null);
            default:
                return (attempt.Fatal ? Outcome.Fatal : Outcome.MoveOn, attempt.Problem);
        }
    }

    /// <summary>The finished part file becomes the recording's file; the way is handed back to be remembered.</summary>
    private void Keep(RecordingAttempt attempt, IReadOnlyList<RecordingStep> chain, string key)
    {
        var route = RecordingSteps.AfterSuccess(chain, attempt.Step, key, _request.Route);
        try
        {
            _log.Step("move the file into place", () => File.Move(attempt.PartPath, _request.Path));
            End(new RecordingResult(_request.Path, attempt.End, EndMessage(attempt.End), route, _log.Text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            End(new RecordingResult(attempt.PartPath, attempt.End, $"The recording was kept as {Path.GetFileName(attempt.PartPath)}: {ex.Message}", route, _log.Text));
        }
    }

    private RecordingResult Failed(string? why) => new(null, RecordingEnd.Failed, why ?? "the recording failed; the details are in the log.", _request.Route, _log.Text);

    /// <summary>Sets the result (a Cancel may already have), then lets a start that was still waiting see it.</summary>
    private void End(RecordingResult result)
    {
        if (_done.TrySetResult(result)) _log.Line($"ended: {result.End}{(result.Message is { } m ? $" ({m})" : "")}");
        _started.TrySetResult(null); // after the result, so a start that failed is seen as failed
    }

    /// <summary>A sentence for the person: part of the start's note before the recording shows, else said at once.</summary>
    private void Tell(string sentence)
    {
        _log.Line($"said: {sentence}");
        if (_started.Task.IsCompleted) _notice?.Invoke(sentence);
        else _notes.Add(sentence);
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private string? EndMessage(RecordingEnd end) => end switch
    {
        RecordingEnd.LengthLimit => $"The recording stopped at its {Words(_request.Limits.MaxLength)} limit.",
        RecordingEnd.SizeLimit => "The recording stopped at its file size limit.",
        RecordingEnd.DiskFull => "The recording stopped because the disk is nearly full.",
        _ => null,
    };

    private static string Words(TimeSpan length) =>
        length.TotalHours >= 1 ? $"{length.TotalHours:0.#} hour" : $"{length.TotalMinutes:0} minute";
}
