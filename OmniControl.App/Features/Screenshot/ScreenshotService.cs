using Avalonia.Media.Imaging;
using OmniControl.App.Features.Osd;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// What a screenshot action captures.
public enum ShotKind { Area, Window, Screen, AllScreens, Record }

/// What the area overlay asks for when the user is done with a picture. The overlay knows nothing else about saving, the clipboard or printing.
public interface IShotOutput
{
    void Copy(ScreenImage image);
    void Save(ScreenImage image);
    void Print(ScreenImage image);

    /// Records a video of this area of the screen (physical pixels on the desktop) on that monitor at its scale; the overlay has already closed.
    void Record(ScreenRect area, ScreenRect monitor, double scale);
    void SearchOnline(ScreenImage image);
    Task<IReadOnlyList<string>> ReadQrAsync(ScreenImage image);
    void CopyText(string text);

    /// Opens a web address in the browser; anything but http and https is refused.
    void OpenLink(string url);
}

/// Takes screenshots and does what the settings say with them: the area action opens the overlay on every monitor; the others capture at
/// once and copy and/or save; "Record area" opens the overlay and records the area as soon as it is drawn (<see cref="VideoRecording"/>).
/// While a recording runs, the area and record actions stop it. Where a picture or a finished recording is saved is <see cref="SavePlaces"/>
/// (the shared save dialog while the action's "Ask where to save" is on). Saving encodes off the UI thread. Opening a folder or the browser goes through the one launch rate limit.
/// Search image online copies the picture and opens Google Lens in the browser; nothing is ever uploaded by OmniControl.
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
    private readonly SavePlaces _places;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private CaptureSession? _session;
    private VideoRecording? _recording;

    /// <param name="hevc">Windows has an HEVC encoder (a recording set to HEVC uses H.264 otherwise).</param>
    /// <param name="ask">The shared confirm (Discard, Force close).</param>
    public ScreenshotService(ScreenshotSettings settings, Action<string> report, Action<OsdMessage> note, bool hevc, Func<DialogMessage, Task<bool>> ask)
    {
        _settings = settings;
        _report = report;
        _note = note;
        _hevc = hevc;
        _ask = ask;
        _places = new SavePlaces(ask, settings.Save);
    }

    /// Takes a screenshot for the action <paramref name="actionId"/> with its settings. UI thread. Ignored while the overlay is open.
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

    /// The emergency key: ends a recording (saved); pressed again while it is still ending, cancels it and keeps no file. Nothing when none runs.
    public void StopRecording()
    {
        if (_recording is not { } recording) return;
        if (recording.StopAsked) recording.AskCancel();
        else recording.Stop();
    }

    private void OpenOverlay(ScreenshotOptions options, bool recordAtOnce)
    {
        var app = ScreenCapture.ActiveApp();
        var shots = new List<(ScreenRect, Bitmap)>();
        try
        {
            foreach (var monitor in ScreenCapture.Monitors()) shots.Add((monitor.Bounds, ImageBitmaps.Capture(monitor.Bounds)));
        }
        catch (InvalidOperationException)
        {
            foreach (var (_, picture) in shots) picture.Dispose(); // the monitors copied before the failure are not kept
            throw;
        }
        _session = new CaptureSession(shots,new Output(this, options, app), options.OnTop, recordAtOnce, new ToolSizes(_settings.ToolSizes, _settings.Save));
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

    /// True for an address the QR pop-down may open: an absolute http or https address.
    internal static bool IsWebLink(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private void Note(string title, string? subtitle = null, string? detail = null) => _note(new OsdMessage(OsdIcon.Screenshot, title, subtitle, detail));

    /// After a file was saved: opens its folder (through the launch limit) and shows the note (its name, and its folder as a third line), as the settings say.
    private void Saved(string path, ScreenshotOptions options, string title)
    {
        if (options.OpenFolder && _launches.TryAcquire() && FileReveal.Show(path) is { } problem) _report(problem);
        if (options.ShowNote) Note(title, Path.GetFileName(path), Path.GetDirectoryName(path));
    }

    /// A finished recording: where it goes (the save dialog when the action asks), moved there, or deleted when discarded.
    private async Task KeepRecordingAsync(string recorded, ScreenshotOptions options)
    {
        var target = await _places.ChooseAsync(SaveKind.Recording, options, Path.GetFileNameWithoutExtension(recorded), Path.GetExtension(recorded), recorded);
        try
        {
            if (target is null)
            {
                File.Delete(recorded);
                return;
            }
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(recorded), StringComparison.OrdinalIgnoreCase))
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(recorded, target);
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _report(target is null ? $"The recording could not be deleted: {ex.Message}" : $"The recording stayed in {Path.GetDirectoryName(recorded)}: {ex.Message}");
            if (target is null) return;
            target = recorded;
        }
        Saved(target, options, "Video saved");
    }

    private void StartRecording(ScreenRect area, ScreenRect monitor, double scale, ScreenshotOptions options, string app)
    {
        if (_recording is not null) return;
        var recording = new VideoRecording(options.Clone(), app, _hevc, _report, _ask,
            path => _ = Guard.RunAsync("Screen recording: save", () => KeepRecordingAsync(path, options)), _settings.RecordingRoute, RememberRoute);
        _recording = recording;
        recording.Ended += () => _recording = null;
        _ = recording.StartAsync(area, monitor, scale);
    }

    /// Keeps the way of recording the recorder handed back for this PC (UI thread).
    private void RememberRoute(RecordingRoute? route)
    {
        if (route == _settings.RecordingRoute) return;
        _settings.RecordingRoute = route;
        _settings.Save();
    }

    /// The picture's way out for one capture: the settings of the action that took it and the program that was active.
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
            if (!ImageClipboard.SetImage(image, _options.Format, _options.Quality, path)) _owner._report("The screenshot was not copied: another program is holding the clipboard.");
        }

        /// Asks where to save when the action says so (<see cref="SavePlaces"/>), encodes and writes the file off the UI thread, then opens the
        /// folder and shows the note as set. The path, or null when it failed or was discarded.
        private async Task<string?> SaveAsync(ScreenImage image)
        {
            var options = _options.Clone(); // what was set when the picture was taken
            var extension = ImageFormats.Extension(options.Format);
            var name = FileNamePattern.Make(options.Pattern, DateTime.Now, _app);
            if (await _owner._places.ChooseAsync(SaveKind.Screenshot, _options, name, extension) is not { } chosen) return null;
            var folder = Path.GetDirectoryName(chosen)!;
            string path;
            try
            {
                path = await Task.Run(() =>
                {
                    var bytes = ImageEncoder.Encode(image, options.Format, options.Quality);
                    Directory.CreateDirectory(folder);
                    var file = FileNamePattern.UniquePath(folder, Path.GetFileNameWithoutExtension(chosen), extension, File.Exists);
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
