using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// One screen recording from start to saved file: makes the file name and the request from the settings (the area made even for MP4,
/// HEVC replaced by H.264 when Windows cannot encode it, the limits from <see cref="Timing"/>), starts the Core recorder, shows the dashed
/// frame and the recording bar, and when the recording ends (Stop, Esc, the action's hotkey again, a limit or a failure) closes them and
/// hands the file on. Throwing it away (the bar's X, Esc twice, the emergency key while it ends) asks first (<see cref="SaveKinds.DiscardQuestion"/>);
/// once Stop has not finished within <see cref="Timing.RecordingStopWait"/> that question offers "Force close". Every recording writes its
/// steps to the app's log (<see cref="Guard.LogPath"/>); a recording that fails says so in one sentence with the log's path and
/// "Copy details" (its log lines). The way of recording the recorder hands back is remembered for this PC (<c>remember</c>). UI thread;
/// nothing here waits on the recorder.
public sealed class VideoRecording
{
    private readonly ScreenshotOptions _options;
    private readonly string _app;
    private readonly bool _hevc;
    private readonly Action<string> _report;
    private readonly Action<string> _saved;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly RecordingRoute? _route;
    private readonly Action<RecordingRoute?> _remember;
    private ScreenRecorder? _recorder;
    private RecordingViewModel? _model;
    private RecordingBar? _bar;
    private RecordingFrame? _frame;
    private readonly DispatcherTimer _stopLimit = new() { Interval = Timing.RecordingStopWait };
    private bool _stopAsked, _finished, _stopStuck, _asking;

    /// The recording is over (saved, failed or never started); the service may start another.
    public event Action? Ended;

    /// <param name="ask">The shared confirm (Discard, Force close).</param>
    /// <param name="saved">Called with the file once it is complete (where it goes, the note and the folder follow the settings).</param>
    /// <param name="route">The way of recording remembered for this PC (null = the graphics card first).</param>
    /// <param name="remember">Keeps the way the recorder hands back at the end.</param>
    public VideoRecording(ScreenshotOptions options, string app, bool hevc, Action<string> report, Func<DialogMessage, Task<bool>> ask, Action<string> saved,
        RecordingRoute? route, Action<RecordingRoute?> remember)
    {
        _route = route;
        _remember = remember;
        _ask = ask;
        _options = options;
        _app = app;
        _hevc = hevc;
        _report = report;
        _saved = saved;
    }

    /// Starts recording <paramref name="area"/> (physical pixels) on <paramref name="monitor"/>, whose scale is <paramref name="scale"/>.
    public async Task StartAsync(ScreenRect area, ScreenRect monitor, double scale)
    {
        var format = _options.VideoFormat == VideoFormat.Mp4Hevc && !_hevc ? VideoFormat.Mp4H264 : _options.VideoFormat;
        var frame = VideoMath.FrameArea(area, format);
        string path;
        try
        {
            path = NewPath(format);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Fail($"The recording did not start: {ex.Message}");
            return;
        }
        var request = new RecordingRequest(frame, path, format, _options.FrameRate, _options.VideoQuality, _options.HardwareEncoder,
            _options.SystemSound, _options.Microphone, Pointer(scale), Limits(format), _route);
        string? note;
        try
        {
            (_recorder, note) = await ScreenRecorder.StartAsync(request, line => AppDataStore.AppendLog(Guard.LogFile, line), _report);
        }
        catch (RecordingFailedException ex)
        {
            Fail($"The recording did not start: {ex.Message}");
            TellFailure($"The recording did not start: {ex.Message}", ex.Details);
            return;
        }
        if (format != _options.VideoFormat) note = $"HEVC is not available on this PC, so the video is H.264. {note}".Trim();
        if (note is not null) _report(note);
        if (!Dispatcher.UIThread.CheckAccess()) await Dispatcher.UIThread.InvokeAsync(() => { }); // the windows below are made on the UI thread whatever thread the start came back on
        var recorder = _recorder;
        if (_finished) return; // cancelled while it was starting; the recorder has already been aborted
        try
        {
            Show(frame, monitor, scale);
        }
        catch (Exception ex)
        {
            Guard.Log("Screen recording: the bar did not open", ex);
            _report("The recording bar did not open, so the recording was cancelled.");
            Cancel();
            return;
        }
        if (_stopAsked) Stop();
        await Finish(recorder);
    }

    /// Ends the recording (Stop, Esc, the action's hotkey again). The file is saved once it is complete; if that takes longer than <see cref="Timing.RecordingStopWait"/> it is said, and the X offers "Force close".
    public bool StopAsked => _stopAsked;

    public void Stop()
    {
        if (_finished) return;
        _stopAsked = true;
        _recorder?.Stop();
        if (_recorder is null || _stopLimit.IsEnabled) return;
        _stopLimit.Tick += OnStopLimit;
        _stopLimit.Start();
    }

    /// The bar's X, Esc twice and the emergency key: asks "Discard this recording?" (or "Force close" once Stop is stuck) and cancels on yes.
    public void AskCancel() => _ = Guard.RunAsync("Screen recording: discard", AskCancelAsync);

    private async Task AskCancelAsync()
    {
        if (_finished || _asking) return;
        _asking = true;
        try
        {
            if (await _ask(_stopStuck ? SaveKinds.ForceCloseQuestion : SaveKinds.DiscardQuestion(SaveKind.Recording)) && !_finished) Cancel();
        }
        finally
        {
            _asking = false;
        }
    }

    /// The kill switch: ends the session and closes the bar and frame at once, keeping no file, whatever the encoder is doing.
    private void Cancel()
    {
        _stopAsked = true;
        if (_recorder is { } recorder)
        {
            var failure = Guard.Run("Screen recording: cancel", recorder.Abort);
            if (failure is not null) _report("The recording was cancelled, but its encoder did not answer.");
        }
        Close();
    }

    private void OnStopLimit(object? sender, EventArgs e)
    {
        _stopLimit.Stop();
        _stopStuck = true;
        _report("The recording is taking long to finish saving; the X on its bar can force it closed.");
    }

    private string NewPath(VideoFormat format)
    {
        var folder = _options.AskVideoFolder ? RecordingStage.Make() : _options.VideoFolderInUse;
        Directory.CreateDirectory(folder);
        var name = FileNamePattern.Make(_options.Pattern, DateTime.Now, _app);
        return FileNamePattern.UniquePath(folder, name, VideoMath.Extension(format), File.Exists);
    }

    private PointerLook Pointer(double scale)
    {
        var c = Palette.Caution;
        return new PointerLook(_options.ShowCursor, _options.HighlightClicks, (uint)(c.R << 16 | c.G << 8 | c.B),
            (int)Math.Round(MarkLook.ClickRadius * scale), MarkLook.ClickOpacity);
    }

    private static RecordingLimits Limits(VideoFormat format) => format == VideoFormat.Gif
        ? new RecordingLimits(Timing.GifMaxLength, Timing.GifMaxBytes, Timing.VideoMinFreeBytes)
        : new RecordingLimits(Timing.VideoMaxLength, Timing.VideoMaxBytes, Timing.VideoMinFreeBytes);

    private void Show(ScreenRect area, ScreenRect monitor, double scale)
    {
        _model = new RecordingViewModel(_recorder!, Stop, AskCancel, _report, _options.SystemSound, _options.Microphone);
        _frame = new RecordingFrame(area, scale);
        _bar = new RecordingBar(_model, area, monitor);
        _frame.Show();
        _bar.Show();
    }

    private async Task Finish(ScreenRecorder recorder)
    {
        var result = await recorder.Completion;
        if (result.End != RecordingEnd.Cancelled) _remember(result.Route);
        if (!Close()) return;
        if (result.Path is { } path) _saved(path);
        if (result.Message is not { } message) return;
        if (result.End != RecordingEnd.Failed)
        {
            _report(message);
            return;
        }
        _report($"The recording stopped: {message}");
        TellFailure($"The recording stopped: {message}", result.Details);
    }

    /// A failed recording in one sentence with the log's path, and its log lines behind "Copy details" for the owner to send.
    private void TellFailure(string sentence, string? details) =>
        _ = Guard.RunAsync("Screen recording: failure message", () => _ask(new DialogMessage("Recording failed",
            $"{sentence} The log is {Guard.LogPath}.", Details: details)));

    /// Closes the bar and frame and tells the service, once. False when it had already been done.
    private bool Close()
    {
        if (_finished) return false;
        _finished = true;
        _stopLimit.Stop();
        _stopLimit.Tick -= OnStopLimit;
        Guard.Run("Screen recording: close the bar", () => _bar?.Close());
        Guard.Run("Screen recording: close the frame", () => _frame?.Close());
        Guard.Run("Screen recording: stop the timer", () => _model?.Dispose());
        Ended?.Invoke();
        return true;
    }

    private void Fail(string message)
    {
        _report(message);
        Ended?.Invoke();
    }
}
