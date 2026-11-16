using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>What a screenshot action captures.</summary>
public enum ShotKind { Area, Window, Screen, AllScreens, Record }

/// <summary>What the area overlay asks for when the user is done with a picture. The overlay knows nothing else about saving, the clipboard or printing.</summary>
public interface IShotOutput
{
    void Copy(ScreenImage image);
    void Save(ScreenImage image);
    void Print(ScreenImage image);

    /// <summary>Records a video of this area of the screen (physical pixels on the desktop) on that monitor at its scale; the overlay has already closed.</summary>
    void Record(ScreenRect area, ScreenRect monitor, double scale);
    void SearchOnline(ScreenImage image);
    Task<IReadOnlyList<string>> ReadQrAsync(ScreenImage image);
    void CopyText(string text);

    /// <summary>Opens a web address in the browser; anything but http and https is refused.</summary>
    void OpenLink(string url);
}

/// <summary>
/// Takes screenshots and does what the settings say with them: the area action opens the overlay on every monitor; the others capture at
/// once and copy and/or save; "Record area" opens the overlay and records the area as soon as it is drawn (<see cref="VideoRecording"/>).
/// While a recording runs, the area and record actions stop it. Saving encodes off the UI thread. Opening a folder or the browser goes through the one launch rate limit.
/// Search image online copies the picture and opens Google Lens in the browser; nothing is ever uploaded by OmniControl.
/// </summary>
public sealed class ScreenshotService
{
    public const string OsdElement = "Screenshots";
    private const string LensUrl = "https://lens.google.com/";
    private const string PrintName = "Screenshot";

    private readonly ScreenshotSettings _settings;
    private readonly Action<string> _report;
    private readonly Action<OsdMessage> _note;
    private readonly LaunchLimiter _launches = new();
    private readonly bool _hevc;
    private CaptureSession? _session;
    private VideoRecording? _recording;

    /// <param name="hevc">Windows has an HEVC encoder (a recording set to HEVC uses H.264 otherwise).</param>
    public ScreenshotService(ScreenshotSettings settings, Action<string> report, Action<OsdMessage> note, bool hevc)
    {
        _settings = settings;
        _report = report;
        _note = note;
        _hevc = hevc;
    }

    /// <summary>Takes a screenshot for the action <paramref name="actionId"/> with its settings. UI thread. Ignored while the overlay is open.</summary>
    public void Take(ShotKind kind, string actionId)
    {
        var area = kind is ShotKind.Area or ShotKind.Record;
        if (_recording is not null && area)
        {
            _recording.Stop(); // the action's hotkey again ends the recording
            return;
        }
        if (_session is not null) return;
        var options = _settings.For(actionId);
        try
        {
            if (area) OpenOverlay(options, kind == ShotKind.Record);
            else Quick(kind, options);
        }
        catch (InvalidOperationException ex)
        {
            _report($"The screenshot was not taken: {ex.Message}");
        }
    }

    private void OpenOverlay(ScreenshotOptions options, bool recordAtOnce)
    {
        var app = ScreenCapture.ActiveApp();
        var shots = ScreenCapture.Monitors().Select(m => (m.Bounds, ScreenCapture.Capture(m.Bounds))).ToList();
        _session = new CaptureSession(shots, new Output(this, options, app), options.OnTop, recordAtOnce, new ToolSizes(_settings.ToolSizes, _settings.Save));
        _session.Closed += () => _session = null;
        _session.Show();
    }

    private void Quick(ShotKind kind, ScreenshotOptions options)
    {
        string app;
        ScreenRect area;
        if (kind == ShotKind.Window)
        {
            if (ScreenCapture.ActiveWindow() is not { } window)
            {
                _report("No window is active, so there is nothing to capture.");
                return;
            }
            (area, app) = (window.Bounds, window.App);
        }
        else
        {
            area = kind == ShotKind.Screen ? ScreenCapture.MonitorUnderPointer() : ScreenCapture.VirtualScreen();
            app = ScreenCapture.ActiveApp();
        }
        var image = ScreenCapture.Capture(area);
        var output = new Output(this, options, app);
        switch (options.Quick)
        {
            case QuickKind.Copy: output.Copy(image); break;
            case QuickKind.Save: output.Save(image); break;
            default: _ = output.SaveAndCopyAsync(image); break;
        }
    }

    /// <summary>True for an address the QR pop-down may open: an absolute http or https address.</summary>
    internal static bool IsWebLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private void Note(string title, string? subtitle = null) => _note(new OsdMessage(OsdIcon.Screenshot, title, subtitle));

    /// <summary>After a file was saved: opens its folder (through the launch limit) and shows the note, as the settings say.</summary>
    private void Saved(string path, ScreenshotOptions options, string title)
    {
        if (options.OpenFolder && _launches.TryAcquire() && FileReveal.Show(path) is { } problem) _report(problem);
        if (options.ShowNote) Note(title, Path.GetFileName(path));
    }

    private void StartRecording(ScreenRect area, ScreenRect monitor, double scale, ScreenshotOptions options, string app)
    {
        if (_recording is not null) return;
        var recording = new VideoRecording(options.Clone(), app, _hevc, _report, path => Saved(path, options, "Video saved"));
        _recording = recording;
        recording.Ended += () => _recording = null;
        _ = recording.StartAsync(area, monitor, scale);
    }

    /// <summary>The picture's way out for one capture: the settings of the action that took it and the program that was active.</summary>
    private sealed class Output : IShotOutput
    {
        private readonly ScreenshotService _owner;
        private readonly ScreenshotOptions _options;
        private readonly string _app;

        public Output(ScreenshotService owner, ScreenshotOptions options, string app)
        {
            _owner = owner;
            _options = options;
            _app = app;
        }

        public void Copy(ScreenImage image) => Copy(image, null);

        public void Save(ScreenImage image) => _ = SaveAsync(image);

        public async Task SaveAndCopyAsync(ScreenImage image)
        {
            var path = await SaveAsync(image);
            Copy(image, _options.CopyAs == CopyKind.ImageAndPath ? path : null);
        }

        private void Copy(ScreenImage image, string? path)
        {
            if (!ImageClipboard.SetImage(image, path)) _owner._report("The screenshot was not copied: another program is holding the clipboard.");
        }

        /// <summary>Encodes and writes the file off the UI thread, then opens the folder and shows the note as set. The path, or null when it failed.</summary>
        private async Task<string?> SaveAsync(ScreenImage image)
        {
            var options = _options.Clone(); // what was set when the picture was taken
            var name = FileNamePattern.Make(options.Pattern, DateTime.Now, _app);
            string path;
            try
            {
                path = await Task.Run(() =>
                {
                    var bytes = ImageEncoder.Encode(image, options.Format, options.Quality);
                    Directory.CreateDirectory(options.FolderInUse);
                    var file = FileNamePattern.UniquePath(options.FolderInUse, name, ImageFormats.Extension(options.Format), File.Exists);
                    File.WriteAllBytes(file, bytes);
                    return file;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _owner._report($"The screenshot was not saved: {ex.Message}");
                return null;
            }
            _owner.Saved(path, options, "Screenshot saved");
            return path;
        }

        public void Print(ScreenImage image)
        {
            if (ImagePrinter.Print(image, PrintName, IntPtr.Zero) == PrintOutcome.Failed)
                _owner._report("The screenshot was not printed: the printer did not accept it.");
        }

        public void SearchOnline(ScreenImage image)
        {
            Copy(image);
            if (!_owner._launches.TryAcquire()) return;
            if (WebPage.Open(LensUrl) is { } problem) _owner._report(problem);
            else _owner.Note("Image copied", "Paste it into Lens");
        }

        public void Record(ScreenRect area, ScreenRect monitor, double scale) => _owner.StartRecording(area, monitor, scale, _options, _app);

        public Task<IReadOnlyList<string>> ReadQrAsync(ScreenImage image) => Task.Run(() => QrReader.Read(image));

        public void CopyText(string text)
        {
            if (!ImageClipboard.SetText(text)) _owner._report("The text was not copied: another program is holding the clipboard.");
        }

        public void OpenLink(string url)
        {
            if (!IsWebLink(url) || !_owner._launches.TryAcquire()) return;
            if (WebPage.Open(url.Trim()) is { } problem) _owner._report(problem);
        }
    }
}
