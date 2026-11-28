using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// The one description of the screenshot settings as the Choose-the-action window shows them (its live settings pane, through
/// <see cref="ActionEditor"/>): the options, how they are read from a <see cref="ScreenshotOptions"/> and how a changed value is stored.
/// The area action and "Record area" also have the "Video" group.
public static class ScreenshotFields
{
    public const string Same = "same", Folder = "folder", Pattern = "pattern", Format = "format", Quality = "quality", CopyAs = "copy",
        Quick = "quick", WindowsKeys = "winkeys", OpenFolder = "open", Note = "note", OnTop = "ontop", AskFolder = "askfolder", Snap = "snap";

    public const string Video = "video", RecFolder = "vfolder", RecFormat = "vformat", FrameRate = "fps", Encoder = "encoder",
        RecQuality = "vquality", SystemSound = "sound", Microphone = "mic", Cursor = "cursor", Clicks = "clicks", AskVideoFolder = "askvfolder";

    private const string HevcMissing = "MP4 (HEVC) needs the HEVC Video Extensions from the Microsoft Store or a graphics card that encodes HEVC.";

    private static readonly string[] CopyNames = { "Image", "Image and file path" };
    private static readonly string[] QuickNames = { "Copy and save", "Copy only", "Save only" };
    private static readonly string[] OnTopNames = { "Normal", "Strong" };
    private static readonly string[] FormatNames = ImageFormats.All.Select(ImageFormats.Name).ToArray();
    private static readonly string[] EncoderNames = { "Auto (graphics card)", "Software" };
    private static readonly string[] QualityNames = { "Low", "Medium", "High" };
    private static readonly string[] FrameRateNames = VideoMath.FrameRates.Select(FrameRateName).ToArray();
    private static readonly string[] Mp4Names = VideoMath.Formats.Where(VideoMath.IsMp4).Select(VideoMath.Name).ToArray();

    // One global switch (not part of any action's set), at the head of every screenshot action's settings.
    private static ActionOption WindowsKeysOption(Func<string?> pendingTag) =>
        ActionOption.Toggle(WindowsKeys, "Turn off Windows' own screenshot keys", note: "Print Screen and Win+Shift+S stop opening Snipping Tool").TaggedWhen(pendingTag);

    private static readonly ActionOption FormatOption = ActionOption.Choice(Format, "Format", FormatNames) with { Default = ImageFormats.Name(ImageFormats.DefaultFormat) };

    /// The settings of one screenshot action. The area action has the overlay's "Stay on top" and the Video group; "Record area" has the file
    /// name, Stay on top and the Video group only; the others say what they do with the picture. <paramref name="hevc"/>: Windows has an HEVC encoder.
    public static IReadOnlyList<ActionOption> Options(ShotKind kind, bool hevc, Func<string?> windowsKeysTag)
    {
        var record = kind == ShotKind.Record;
        var options = new List<ActionOption> { ActionOption.Toggle(Same, "Same settings for every screenshot action", on: true), WindowsKeysOption(windowsKeysTag) };
        if (!record)
        {
            options.Add(ActionOption.Path(Folder, "Save folder", PathKind.Folder, ScreenshotOptions.DefaultFolder));
            options.Add(ActionOption.Toggle(AskFolder, "Ask where to save", on: true, note: "Name and folder before each screenshot is saved"));
            options.Add(ActionOption.Text(Pattern, "File name", ScreenshotOptions.DefaultPattern, "{date} {time} {app}"));
            options.Add(FormatOption);
            options.Add(ActionOption.Number(Quality, "Quality", ImageFormats.DefaultQuality, ImageFormats.MinQuality, ImageFormats.MaxQuality, 5, " %")
                .ShownWhen(FormatOption, ImageFormats.All.Where(ImageFormats.UsesQuality).Select(ImageFormats.Name).ToArray()));
            options.Add(ActionOption.Choice(CopyAs, "Copy as", CopyNames));
        }
        else options.Add(ActionOption.Text(Pattern, "File name", ScreenshotOptions.DefaultPattern, "{date} {time} {app}"));
        options.Add(kind is ShotKind.Area or ShotKind.Record ? ActionOption.Choice(OnTop, "Stay on top", OnTopNames) : ActionOption.Choice(Quick, "Then", QuickNames));
        if (kind is ShotKind.Area or ShotKind.Record)
            options.Add(ActionOption.Toggle(Snap, "Snap to windows", on: true, note: "Edges snap to window borders and a click selects a whole window; hold Alt to ignore it for one drag"));
        if (kind is ShotKind.Area or ShotKind.Record) options.AddRange(VideoOptions(hevc));
        options.Add(ActionOption.Toggle(OpenFolder, "Open the folder after saving"));
        options.Add(ActionOption.Toggle(Note, "Show a note after saving", on: true));
        return options;
    }

    /// The Video group: MP4 (HEVC) is offered only when Windows can encode it, and the heading says why when it cannot.
    private static IEnumerable<ActionOption> VideoOptions(bool hevc)
    {
        var formats = VideoMath.Formats.Where(f => hevc || f != VideoFormat.Mp4Hevc).Select(VideoMath.Name).ToArray();
        var format = ActionOption.Choice(RecFormat, "Format", formats);
        yield return ActionOption.Heading(Video, "Video", hevc ? "" : HevcMissing);
        yield return format;
        yield return ActionOption.Choice(FrameRate, "Frame rate", FrameRateNames) with { Default = FrameRateName(VideoMath.DefaultFrameRate) };
        yield return ActionOption.Choice(Encoder, "Encoder", EncoderNames).ShownWhen(format, Mp4Names);
        yield return ActionOption.Choice(RecQuality, "Quality", QualityNames) with { Default = QualityNames[(int)VideoQuality.Medium] };
        yield return ActionOption.Toggle(SystemSound, "System sound", on: true).ShownWhen(format, Mp4Names);
        yield return ActionOption.Toggle(Microphone, "Microphone").ShownWhen(format, Mp4Names);
        yield return ActionOption.Toggle(Cursor, "Show cursor", on: true);
        yield return ActionOption.Toggle(Clicks, "Highlight clicks");
        yield return ActionOption.Path(RecFolder, "Save folder", PathKind.Folder, ScreenshotOptions.DefaultVideoFolder);
        yield return ActionOption.Toggle(AskVideoFolder, "Ask where to save", on: true, note: "Name and folder after each recording");
    }

    private static string FrameRateName(int rate) => $"{rate} fps";

    /// The value of every setting, as the settings pane shows it.
    public static IReadOnlyDictionary<string, string> Read(ScreenshotOptions o, bool same, bool windowsKeys) => new Dictionary<string, string>
    {
        [Same] = Flag(same),
        [WindowsKeys] = Flag(windowsKeys),
        [Folder] = o.Folder,
        [AskFolder] = Flag(o.AskFolder),
        [Pattern] = o.Pattern,
        [Format] = ImageFormats.Name(o.Format),
        [Quality] = o.Quality.ToString(),
        [CopyAs] = CopyNames[(int)o.CopyAs],
        [Quick] = QuickNames[(int)o.Quick],
        [OpenFolder] = Flag(o.OpenFolder),
        [Note] = Flag(o.ShowNote),
        [OnTop] = OnTopNames[(int)o.OnTop],
        [Snap] = Flag(o.SnapToWindows),
        [RecFolder] = o.VideoFolder,
        [AskVideoFolder] = Flag(o.AskVideoFolder),
        [RecFormat] = VideoMath.Name(o.VideoFormat),
        [FrameRate] = FrameRateName(o.FrameRate),
        [Encoder] = EncoderNames[o.HardwareEncoder ? 0 : 1],
        [RecQuality] = QualityNames[(int)o.VideoQuality],
        [SystemSound] = Flag(o.SystemSound),
        [Microphone] = Flag(o.Microphone),
        [Cursor] = Flag(o.ShowCursor),
        [Clicks] = Flag(o.HighlightClicks),
    };

    /// Stores one changed setting (not <see cref="Same"/> or <see cref="WindowsKeys"/>, which the owner of the shared set handles).
    public static void Apply(ScreenshotOptions o, string key, string value)
    {
        switch (key)
        {
            case Folder: o.Folder = value.Trim(); break;
            case AskFolder: o.AskFolder = value == "1"; break;
            case Pattern: o.Pattern = value.Trim().Length > 0 ? value.Trim() : ScreenshotOptions.DefaultPattern; break;
            case Format: o.Format = ImageFormats.Parse(value); break;
            case Quality:
                if (int.TryParse(value, out var quality)) o.Quality = Math.Clamp(quality, ImageFormats.MinQuality, ImageFormats.MaxQuality);
                break;
            case CopyAs: o.CopyAs = (CopyKind)IndexOf(CopyNames, value); break;
            case Quick: o.Quick = (QuickKind)IndexOf(QuickNames, value); break;
            case OpenFolder: o.OpenFolder = value == "1"; break;
            case Note: o.ShowNote = value == "1"; break;
            case OnTop: o.OnTop = (StayOnTop)IndexOf(OnTopNames, value); break;
            case Snap: o.SnapToWindows = value == "1"; break;
            case RecFolder: o.VideoFolder = value.Trim(); break;
            case AskVideoFolder: o.AskVideoFolder = value == "1"; break;
            case RecFormat: o.VideoFormat = VideoMath.Parse(value); break;
            case FrameRate: o.FrameRate = VideoMath.FrameRates[IndexOf(FrameRateNames, value)]; break;
            case Encoder: o.HardwareEncoder = IndexOf(EncoderNames, value) == 0; break;
            case RecQuality: o.VideoQuality = (VideoQuality)IndexOf(QualityNames, value); break;
            case SystemSound: o.SystemSound = value == "1"; break;
            case Microphone: o.Microphone = value == "1"; break;
            case Cursor: o.ShowCursor = value == "1"; break;
            case Clicks: o.HighlightClicks = value == "1"; break;
        }
    }

    private static string Flag(bool on) => on ? "1" : "0";

    private static int IndexOf(string[] names, string value) => Math.Max(0, Array.IndexOf(names, value));
}
