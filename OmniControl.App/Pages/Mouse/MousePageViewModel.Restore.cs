using OmniControl.App.Mouse;
using OmniControl.App.Pages.Restore;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// "Restore defaults" on the Mouse and Trackpad page (<see cref="MouseRestoreState"/>): every mapping, layer and option goes through the
/// mouse service (which gives back any Windows gesture no mapping takes over any more), and the pointer settings go back to the user's own
/// from before OmniControl changed them, through the pointer service.
/// </summary>
public sealed partial class MousePageViewModel
{
    private IRestorable? _restorable;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<MouseRestoreState>("mouse", Title, ScopeText,
        () => new MouseRestoreState { Mouse = Settings, Pointer = _pointer.HasOriginal ? _pointer.Capture() : null },
        () => MouseRestoreState.DefaultsOf(new MouseRestoreState { Pointer = _pointer.HasOriginal ? _pointer.Capture() : null }, _pointer.Original),
        ApplyState, MouseRestoreState.Same);

    private string ScopeText() =>
        "Puts every mouse and trackpad mapping, layer, layer name and option back to the defaults and gives Windows its own trackpad gestures back. "
        + (_pointer.HasOriginal
            ? "The pointer settings go back to what you had before OmniControl first changed them."
            : "The pointer settings stay: OmniControl never changed them.");

    private async Task<bool> ApplyState(MouseRestoreState target)
    {
        _mouse.ReplaceSettings(target.Mouse);
        _trackpad.Refresh();
        if (target.Pointer is not { } pointer) return true;
        var ok = await _pointer.RestoreTo(pointer);
        LoadPointerSettings();
        return ok;
    }
}
