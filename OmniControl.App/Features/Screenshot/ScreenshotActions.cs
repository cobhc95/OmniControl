using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Osd;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// The screenshot actions and "Record area", registered once in the master list (so they appear on the Shortcuts page, in the mouse, gesture and rule
/// pickers and in search) and listed under their own "Screenshot" section of the Choose-the-action window. Each has the same live settings
/// pane (<see cref="ScreenshotFields"/>); its "Same settings for every screenshot action" switch, on by default, makes a change apply to all four.
public static class ScreenshotActions
{
    public const string Category = ActionSections.Screenshot;
    public const string StopRecordingId = "screenshot.stoprecording";
    private const string Words = "screenshot screen capture snip snipping print screen grab picture image";
    private const string RecordWords = "record recording video screen recorder capture mp4 gif clip";

    public static (WindowsKeysSwitch Keys, ScreenshotService Service) Register(IActionRegistry hotkeys, IOsd osd, Action<string> report, Func<DialogMessage, Task<bool>> ask)
    {
        osd.DeclareText(ScreenshotService.OsdElement);
        var settings = ScreenshotSettings.Load();
        var hevc = VideoEncoders.HevcAvailable();
        var windowsKeys = new WindowsKeysSwitch(settings, report);
        windowsKeys.Reapply();
        var service = new ScreenshotService(settings, report, message => osd.ShowMessage(ScreenshotService.OsdElement, message), hevc, ask);
        hotkeys.Register(new[]
        {
            Make("screenshot.area", "Screenshot: area (edit)", ShotKind.Area, $"{Words} region select draw annotate lightshot snap magnet window edges pick", service, settings, hevc, windowsKeys),
            Make("screenshot.window", "Screenshot: active window", ShotKind.Window, $"{Words} window app", service, settings, hevc, windowsKeys),
            Make("screenshot.screen", "Screenshot: full screen", ShotKind.Screen, $"{Words} monitor display under pointer", service, settings, hevc, windowsKeys),
            Make("screenshot.all", "Screenshot: all screens", ShotKind.AllScreens, $"{Words} every monitor desktop", service, settings, hevc, windowsKeys),
            Make("screenshot.record", "Record area", ShotKind.Record, RecordWords, service, settings, hevc, windowsKeys),
            new HotkeyAction(StopRecordingId, "Stop screen recording", Category, $"{RecordWords} stop end emergency cancel kill", _ => Dispatcher.UIThread.Post(service.StopRecording))
                { PickerSection = ActionSections.Screenshot, Icon = IconLibrary.Video, NeverBlock = true, DefaultKey = "Ctrl+Alt+F12" },
            new HotkeyAction(WindowsKeysSwitch.ActionId, "Turn off Windows' screenshot keys: toggle", Category, $"{Words} windows keys snipping tool print screen win shift s disable block",
                _ => windowsKeys.Toggle()) { PickerSection = ActionSections.Screenshot, Icon = IconLibrary.Screen },
        });
        return (windowsKeys, service);
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

    private static HotkeyAction Make(string id, string title, ShotKind kind, string keywords, ScreenshotService service, ScreenshotSettings settings, bool hevc, WindowsKeysSwitch windowsKeys) =>
        new(id, title, Category, keywords, _ => Dispatcher.UIThread.Post(() => service.Take(kind, id)))
        {
            PickerSection = ActionSections.Screenshot,
            Short = ShortNames.GetValueOrDefault(kind),
            Icon = Icons.GetValueOrDefault(kind),
            Editor = new ActionEditor(ScreenshotFields.Options(kind, hevc, () => windowsKeys.PendingTag),
                () => ScreenshotFields.Read(settings.For(id), settings.UsesShared(id), windowsKeys.IsOn),
                (key, value) => Edit(settings, windowsKeys, id, key, value)),
        };

    /// Stores one change from the settings pane at once: the Windows keys switch is global (<see cref="WindowsKeysSwitch"/>); the other switch picks the shared or the action's own set; any other value goes into the set in use.
    private static string? Edit(ScreenshotSettings settings, WindowsKeysSwitch windowsKeys, string id, string key, string value)
    {
        if (key == ScreenshotFields.WindowsKeys)
        {
            windowsKeys.Set(value == "1");
            return null;
        }
        if (key == ScreenshotFields.Same) settings.SetShared(id, value == "1");
        else ScreenshotFields.Apply(settings.For(id), key, value);
        settings.Save();
        return null;
    }
}
