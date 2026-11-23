using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays.Pages;

public sealed partial class DisplaysService
{
    private void RegisterRotationState(HotkeyAction action, DisplayOrientation orientation)
    {
        var name = DisplayOrientationModel.Title(orientation);
        _states.Register(new StateEntry(action.Id, $"Displays in {name}", Title, StateKind.OnOff,
            () => ReadRotation(action.SelectedDeviceIds(), orientation), "Windows' reported orientation of all displays in the rotation action's scope.",
            Heading: DisplayOrientationModel.Label, Keywords: RotationWords, Short: action.ShortName, Icon: action.ShownIcon,
            Wording: new StateWords("the selected displays", $"rotate to {name}", $"leave {name}", $"enter or leave {name}")));
        action.Changed += OnRotationScopeChanged;
    }

    private StateValue? ReadRotation(IReadOnlyList<string>? ids, DisplayOrientation orientation)
    {
        var any = false;
        var all = true;
        foreach (var display in _hardware)
        {
            if (ids is not null && !ids.Contains(display.Id)) continue;
            if (display.Orientation.Current is not { } current) return null;
            any = true;
            all &= current == orientation;
        }
        return any && (ids is null || ids.All(id => _hardware.Any(h => h.Id == id))) ? StateValue.Of(all) : null;
    }

    private void OnRotationScopeChanged(HotkeyAction action) => _states.Notify(action.Id);

    private IEnumerable<HotkeyAction> RotationActions() => _scopedActions.Where(a => a.SuggestedKeySet == RotationKeySet);

    private void NotifyOrientation() => _states.Notify(RotationActions().Select(a => a.Id).ToList());
}
