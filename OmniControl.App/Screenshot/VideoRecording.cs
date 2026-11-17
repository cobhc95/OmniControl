using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// One screen recording from start to saved file: makes the file name and the request from the settings (the area made even for MP4,
/// HEVC replaced by H.264 when Windows cannot encode it, the limits from <see cref="Timing"/>), starts the Core recorder, shows the dashed
/// frame and the recording bar, and when the recording ends (Stop, Esc, the action's hotkey again, a limit or a failure) closes them and
/// hands the file on. UI thread.
/// </summary>
public sealed class VideoRecording
{
    private readonly ScreenshotOptions _options;
    private readonly string _app;
    private readonly bool _hevc;
    private readonly Action<string> _report;
    private readonly Action<string> _saved;
    private ScreenRecorder? _recorder;
    private RecordingViewModel? _model;
    private RecordingBar? _bar;
    private RecordingFrame? _frame;
    private bool _stopAsked;

    /// <summary>The recording is over (saved, failed or never started); the service may start another.</summary>
    public event Action? Ended;

    /// <param name="saved">Called with the file once it is complete (the folder and the note follow the settings).</param>
    public VideoRecording(ScreenshotOptions options, string app, bool hevc, Action<string> report, Action<string> saved)
    {
        _options = options;
        _app = app;
        _hevc = hevc;
        _report = report;
        _saved = saved;
    }

    /// <summary>Starts recording <paramref name="area"/> (physical pixels) on <paramref name="monitor"/>, whose scale is <paramref name="scale"/>.</summary>
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
            _options.SystemSound, _options.Microphone, Pointer(scale), Limits(format));
        string? note;
        try
        {
            (_recorder, note) = await ScreenRecorder.StartAsync(request, line => AppDataStore.AppendLog(Guard.LogFile, line));
        }
        catch (InvalidOperationException ex)
        {
            Fail($"The recording did not start: {ex.Message}");
            return;
        }
        if (format != _options.VideoFormat) note = $"HEVC is not available on this PC, so the video is H.264. {note}".Trim();
        if (note is not null) _report(note);
        Show(frame, monitor, scale);
        if (_stopAsked) _recorder.Stop();
        await Finish(_recorder);
    }

    /// <summary>Ends the recording (Stop, Esc, the action's hotkey again). The file is saved once it is complete.</summary>
    public void Stop()
    {
        _stopAsked = true;
        _recorder?.Stop();
    }

    private string NewPath(VideoFormat format)
    {
        var folder = _options.VideoFolderInUse;
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
        _model = new RecordingViewModel(_recorder!, Stop, _report, _options.SystemSound, _options.Microphone);
        _frame = new RecordingFrame(area, scale);
        _bar = new RecordingBar(_model, area, monitor);
        _frame.Show();
        _bar.Show();
    }

    private async Task Finish(ScreenRecorder recorder)
    {
        var result = await recorder.Completion;
        _bar?.Close();
        _frame?.Close();
        _model?.Dispose();
        if (result.Path is { } path) _saved(path);
        if (result.Message is { } message)
            _report(result.End == RecordingEnd.Failed ? $"The recording stopped: {message}" : message);
        Ended?.Invoke();
    }

    private void Fail(string message)
    {
        _report(message);
        Ended?.Invoke();
    }
}
