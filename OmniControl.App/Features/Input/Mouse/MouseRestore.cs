using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Rules;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Input.Mouse;

/// The Mouse and Trackpad page's "Restore defaults" (<see cref="MouseRestoreState"/>), over the services alone: every mapping, layer and option
/// goes through the mouse service (which gives back any Windows gesture no mapping takes over any more), the gestures and custom rules through
/// theirs, and the pointer settings go back to the user's own from before OmniControl changed them. The question carries a stronger line when
/// custom rules exist.
/// <param name="shown">Shows the result on the page when it exists.</param>
public sealed class MouseRestore(MouseService mouse, TrackpadService trackpad, PointerSettingsService pointer, GestureService gestures, RuleService rules, Action shown)
{
    public IRestorable Make(string key, string title) => new SettingsRestore<MouseRestoreState>(key, title, Scope,
        () => new MouseRestoreState { Mouse = mouse.Settings, Pointer = PointerNow(), Gestures = gestures.Settings, Rules = rules.Settings },
        () => MouseRestoreState.DefaultsOf(new MouseRestoreState { Pointer = PointerNow() }, pointer.Original),
        ApplyAsync, MouseRestoreState.Same, warning: () => MouseRestoreState.WarningOf(rules.Settings.Rules.Count));

    private PointerOriginal? PointerNow() => pointer.HasOriginal ? pointer.Capture() : null;

    private string Scope() =>
        "Puts every mouse and trackpad mapping, layer, layer name and option back to the defaults and gives Windows its own trackpad gestures back. "
        + "Turns Enable gestures on and puts the edge strip width and every built-in gesture back to its default with its default action, every app. "
        + (pointer.HasOriginal
            ? "The pointer settings go back to what you had before OmniControl first changed them."
            : "The pointer settings stay: OmniControl never changed them.");

    private async Task<bool> ApplyAsync(MouseRestoreState target)
    {
        mouse.ReplaceSettings(target.Mouse);
        trackpad.Refresh();
        gestures.Replace(target.Gestures);
        rules.ReplaceAll(target.Rules);
        var ok = target.Pointer is not { } before || await pointer.RestoreTo(before);
        shown();
        return ok;
    }
}
