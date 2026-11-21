using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Input.Pages.Mouse;
using OmniControl.App.Features.Osd;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Input;

/// The Mouse and Trackpad page's entries in the master lists, owned by the module so they work before the page is made: the gesture
/// switches, pause in full-screen games, the next-layer and autoscroll actions, the pointer settings' actions, repeated clicking with its
/// OSD badge (the engine is <see cref="RepeatedClicking"/>; a hotkey runs it as a toggle with its default settings, a button, gesture or rule
/// with the settings it chose), and the two layer preset groups, in the order the page registered them.
internal sealed class InputActions(MouseService mouse, GestureService gestures, MarginOverlay margins, PointerActions pointer, IOsd osd) : IDisposable
{
    private const string Title = ActionSections.Mouse;
    private const string ClickBadgeElement = "Repeated clicking";

    private readonly Subscriptions _followed = new();

    public void Register(IActionRegistry hotkeys, IPresetGroups presets)
    {
        hotkeys.Register(new HotkeyAction("gestures.toggle", "Toggle gestures", Title, "gestures mouse wheel scroll on off custom rules",
            _ => gestures.SetEnabled(!gestures.Settings.Enabled)));
        hotkeys.Register(new HotkeyAction("gestures.margins", "Toggle gesture margins", Title, "gestures show margins edge strip width areas screen",
            _ => margins.SetWanted(!margins.Wanted)));
        var pauseFamily = new OnOffFamily(MouseService.PauseFullScreenId, "Pause in full-screen games", MouseService.PauseFullScreenId);
        var pause = new HotkeyAction(MouseService.PauseFullScreenId, "Toggle pause in full-screen games", Title, "full screen fullscreen game pause mouse buttons gestures",
            _ => mouse.TogglePauseInFullScreen()) { Family = pauseFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(pauseFamily.Siblings(pause, mouse.SetPauseInFullScreen).Prepend(pause));
        hotkeys.Register(new HotkeyAction("mouse.layer.next", "Next mouse layer", Title, "mouse layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Mouse)) { CoveredByMousePicker = true });
        hotkeys.Register(new HotkeyAction("mouse.trackpad.layer.next", "Next trackpad layer", Title, "trackpad touchpad gestures layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Trackpad)) { CoveredByMousePicker = true });
        var autoscrollFamily = new OnOffFamily(MouseService.AutoscrollId, "App autoscroll block", MouseService.AutoscrollId);
        var autoscroll = new HotkeyAction(MouseService.AutoscrollId, "Toggle app autoscroll", Title, "middle click autoscroll scroll browser block apps",
            _ => mouse.ToggleBlockAutoscroll()) { Family = autoscrollFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(autoscrollFamily.Siblings(autoscroll, mouse.SetBlockAutoscroll).Prepend(autoscroll));
        pointer.Register(hotkeys, Title);
        RegisterClicking(hotkeys);
        // Last: a preset group's card is named after the page group it belongs to, and a layer with a saved shortcut gets its action
        // after the page's own actions.
        presets.Register(new PresetGroup(LayerPresets.MouseGroupId, Title, MousePageViewModel.ButtonsGroupName, new LayerPresets(mouse, InputFamily.Mouse)) { MovesLayer = true });
        presets.Register(new PresetGroup(LayerPresets.TrackpadGroupId, Title, MousePageViewModel.TrackpadGroupName, new LayerPresets(mouse, InputFamily.Trackpad)) { MovesLayer = true });
    }

    private void RegisterClicking(IActionRegistry hotkeys)
    {
        var clicking = mouse.Clicking;
        osd.DeclareText(ClickBadgeElement);
        _followed.Follow<Action<long?>>(h => clicking.Progress += h, h => clicking.Progress -= h, clicks => Dispatcher.UIThread.Post(() =>
        {
            if (clicks is { } count) osd.ShowMessage(ClickBadgeElement, new OsdMessage(OsdIcon.Play, "Clicking...", count.ToString()));
        }));
        hotkeys.Register(new HotkeyAction("mouse.repeatclick", "Repeated clicking", Title, "auto clicker autoclicker click repeat button hold toggle",
            _ => clicking.Toggle(OptionValues.None), group: "Clicks and wheel")
        {
            Accepts = ActionSettings.Clicking,
            RunConfigured = settings => clicking.Toggle(settings.Options ?? OptionValues.None),
            RunHeld = settings => clicking.StartHeld(settings.Options ?? OptionValues.None),
            SuggestedIcons = new[] { "mouse" },
        });
    }

    public void Dispose() => _followed.Dispose();
}
