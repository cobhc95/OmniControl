using OmniControl.App.Hotkeys;
using OmniControl.App.Pages.Restore;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>"Restore defaults" on the Shortcuts page: every key combination removed and every action back on all its devices.</summary>
public sealed partial class ShortcutsPageViewModel
{
    private IRestorable? _restorable;

    public override IRestorable? Restorable => _restorable ??= new SettingsRestore<Dictionary<string, StoredHotkey>>("shortcuts", Title,
        () => "Removes every keyboard shortcut and puts each action back on all its devices.",
        _registry.ExportAll, () => HotkeyDefaults.Of(_registry.ExportAll()),
        all => { _registry.ImportAll(all); return Task.FromResult(true); },
        atDefaults: HotkeyDefaults.IsAtDefaults);
}
