using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// "Restore defaults" on the Mouse and Trackpad page (<see cref="MouseRestoreState"/>): every mapping, layer and option goes through the
/// mouse service (which gives back any Windows gesture no mapping takes over any more), the gestures and custom rules through the gesture
/// groups, and the pointer settings go back to the user's own from before OmniControl changed them, through the pointer service. The
/// question carries a stronger line when custom rules exist.
public sealed partial class MousePageViewModel
{
    private IRestorable? _restorable;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<MouseRestoreState>(Info.RestoreKey!, Title, ScopeText,
        () => new MouseRestoreState { Mouse = Settings, Pointer = PointerNow(), Gestures = Gestures.CurrentGestures, Rules = Gestures.CurrentRules },
        () => MouseRestoreState.DefaultsOf(new MouseRestoreState { Pointer = PointerNow() }, _pointer.Original),
        ApplyState, MouseRestoreState.Same, warning: () => MouseRestoreState.WarningOf(Gestures.CurrentRules.Rules.Count));

    private PointerOriginal? PointerNow() => _pointer.HasOriginal ? _pointer.Capture() : null;

    private string ScopeText() =>
        "Puts every mouse and trackpad mapping, layer, layer name and option back to the defaults and gives Windows its own trackpad gestures back. "
        + "Turns Enable gestures on and puts the edge strip width and every built-in gesture back to its default with its default action, every app. "
        + (_pointer.HasOriginal
            ? "The pointer settings go back to what you had before OmniControl first changed them."
            : "The pointer settings stay: OmniControl never changed them.");

    private async Task<bool> ApplyState(MouseRestoreState target)
    {
        _mouse.ReplaceSettings(target.Mouse);
        _trackpad.Refresh();
        Gestures.Replace(target.Gestures, target.Rules);
        if (target.Pointer is not { } pointer) return true;
        var ok = await _pointer.RestoreTo(pointer);
        LoadPointerSettings();
        return ok;
    }
}
