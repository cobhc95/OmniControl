using OmniControl.App.Features.Displays;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Displays.Pages;

/// "Restore defaults" on the Displays page: the software picture and warm light (<see cref="DisplayRestoreState"/>); the monitors' own settings are never written.
public sealed partial class DisplaysService
{
    /// The page's Restore defaults (<paramref name="key"/>: its restore key).
    public IRestorable NewRestorable(string key) => new SettingsRestore<DisplayRestoreState>(key, Title,
        () => DisplayRestoreState.Scope,
        () => new DisplayRestoreState { Picture = CapturePicture(), WarmLight = _warmLight.Model.IsOn },
        () => DisplayRestoreState.DefaultsOf(new DisplayRestoreState { Picture = CapturePicture() }),
        ApplyState);

    /// Takes display-settings.json from disk as it is now (an import wrote it): the preset lists follow and the warm-light schedule is applied again.
    public bool ReloadSettings()
    {
        var done = _store.ReloadFromDisk();
        _warmLight.Update();
        return done;
    }

    /// Shows and applies the pictures through the same path a preset takes, and sets warm light through its model.
    private Task<bool> ApplyState(DisplayRestoreState state)
    {
        ApplyPicture(state.Picture);
        _warmLight.Model.SetEnabled(state.WarmLight);
        return Task.FromResult(true);
    }
}
