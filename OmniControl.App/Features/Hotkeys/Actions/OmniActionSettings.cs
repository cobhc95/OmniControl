using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Actions;

/// The master list's view of an action's settings (<see cref="IActionSettings"/>), from the options and catalogue of the mouse actions.
public sealed class OmniActionSettings : IActionSettings
{
    public IReadOnlyList<ActionOption> OptionsOf(HotkeyAction action) => MouseActionCatalog.OwnOptions(action);

    public RunSettings RunOf(OptionValues values) => MouseOptions.RunOf(values);
}
