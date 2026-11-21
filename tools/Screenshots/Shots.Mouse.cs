using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Input;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Input.Pages.Gestures;
using OmniControl.App.Features.Input.Pages.Mouse;
using OmniControl.App.Features.Input.Rules;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    private static MousePageViewModel? _mouse;

    /// The Mouse and Trackpad page with a few button mappings. The mouse hook is made but never started (no SetWindowsHookEx); no trackpad
    /// gesture gets an action, so Windows' own touchpad gestures are never turned off; the pointer settings are only read.
    private static void Mouse(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<MousePageViewModel, MousePage>(),
            ViewTemplate.For<MouseButtonItem, MouseButtonRow>(),
            ViewTemplate.For<GestureItem, GestureRow>(),
            ViewTemplate.For<RuleItem, RuleRow>());

        var settings = new MouseSettings { RunAsAdministrator = false };
        settings.Bindings["1.Middle"] = "media.playpause";
        settings.Bindings["1.Back"] = "desktop.taskview";
        settings.Bindings["1.Forward"] = "edit.copy";
        settings.Bindings["1.TiltLeft"] = "browser.back";
        settings.Bindings["1.TiltRight"] = "browser.forward";
        settings.Save();

        var hook = new MouseHook(); // never started
        var gestures = new GestureService(hook, rig.Runner.RunLevel, Rig.Report, rig.ScopeGate);
        var mouse = new MouseService(hook, rig.Runner.Run, rig.Runner.RunHeld, Rig.Report, gestures.MiddleClickAt, rig.ScopeGate, rig.States, _ => null);
        var rules = new RuleService(hook, gestures, mouse, rig.Actions, rig.Runner, Array.Empty<string>, Rig.Report, Rig.Ask);
        var trackpad = new TrackpadService(hook, mouse, rig.Hardware, () => false);
        var pointer = new PointerSettingsService();
        var margins = new MarginOverlay(gestures.Settings);
        var pointerActions = new PointerActions(pointer, rig.Osd, Rig.Ask, Rig.Report);
        new InputActions(mouse, gestures, margins, pointerActions, rig.Osd).Register(rig.Actions, rig.Presets);
        var page = _mouse = new MousePageViewModel(mouse, trackpad, pointer, pointerActions, rig.Actions, Rig.Report, Rig.OpenPresets, rig.Scopes,
            new GestureGroups(gestures, rules, margins, rig.Actions, Rig.Ask, rig.Scopes));
        rig.Pages.Add(page);
        Camera.Pump();
        page.ButtonsGroup.IsExpanded = true;
        page.TrackpadGroup.IsExpanded = false;
        camera.Page(page, "mouse");
    }
}
