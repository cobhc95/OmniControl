using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Icons;
using OmniControl.App.Osd;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// The screenshot actions and "Record area", registered once in the master list (so they appear on the Shortcuts page, in the mouse, gesture and rule
/// pickers and in search) and listed under their own "Screenshot" section of the Choose-the-action window. Each has the same live settings
/// pane (<see cref="ScreenshotFields"/>); its "Same settings for every screenshot action" switch, on by default, makes a change apply to all four.
/// </summary>
public static class ScreenshotActions
{
    public const string Category = ActionSections.Screenshot;
    private const string Words = "screenshot screen capture snip snipping print screen grab picture image";
    private const string RecordWords = "record recording video screen recorder capture mp4 gif clip";

    public static void Register(HotkeyRegistry hotkeys, OsdService osd, Action<string> report)
    {
        osd.DeclareText(ScreenshotService.OsdElement);
        var settings = ScreenshotSettings.Load();
        var hevc = VideoEncoders.HevcAvailable();
        var service = new ScreenshotService(settings, report, message => osd.ShowMessage(ScreenshotService.OsdElement, message), hevc);
        hotkeys.Register(new[]
        {
            Make("screenshot.area", "Screenshot: area (edit)", ShotKind.Area, $"{Words} region select draw annotate lightshot", service, settings, hevc),
            Make("screenshot.window", "Screenshot: active window", ShotKind.Window, $"{Words} window app", service, settings, hevc),
            Make("screenshot.screen", "Screenshot: full screen", ShotKind.Screen, $"{Words} monitor display under pointer", service, settings, hevc),
            Make("screenshot.all", "Screenshot: all screens", ShotKind.AllScreens, $"{Words} every monitor desktop", service, settings, hevc),
            Make("screenshot.record", "Record area", ShotKind.Record, RecordWords, service, settings, hevc),
        });
    }

    private static readonly Dictionary<ShotKind, string> ShortNames = new()
    {
        [ShotKind.Area] = "Area", [ShotKind.Window] = "Window", [ShotKind.Screen] = "Screen", [ShotKind.AllScreens] = "All screens", [ShotKind.Record] = "Record",
    };

    private static readonly Dictionary<ShotKind, string> Icons = new()
    {
        [ShotKind.Area] = IconLibrary.Area, [ShotKind.Window] = IconLibrary.Window, [ShotKind.Screen] = IconLibrary.Screen,
        [ShotKind.AllScreens] = IconLibrary.Desktop, [ShotKind.Record] = IconLibrary.Video,
    };

    private static HotkeyAction Make(string id, string title, ShotKind kind, string keywords, ScreenshotService service, ScreenshotSettings settings, bool hevc) =>
        new(id, title, Category, keywords, _ => Dispatcher.UIThread.Post(() => service.Take(kind, id)))
        {
            PickerSection = ActionSections.Screenshot,
            Short = ShortNames.GetValueOrDefault(kind),
            Icon = Icons.GetValueOrDefault(kind),
            Editor = new ActionEditor(ScreenshotFields.Options(kind, hevc),
                () => ScreenshotFields.Read(settings.For(id), settings.UsesShared(id)),
                (key, value) => Edit(settings, id, key, value)),
        };

    /// <summary>Stores one change from the settings pane at once: the switch picks the shared or the action's own set; any other value goes into the set in use.</summary>
    private static string? Edit(ScreenshotSettings settings, string id, string key, string value)
    {
        if (key == ScreenshotFields.Same) settings.SetShared(id, value == "1");
        else ScreenshotFields.Apply(settings.For(id), key, value);
        settings.Save();
        return null;
    }
}
