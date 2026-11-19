using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Screenshot;

/// The screenshot actions, and Windows' own screenshot keys that their settings can turn off.
public sealed class ScreenshotModule : IFeatureModule
{
    private WindowsKeysSwitch? _windowsKeys;

    public void Register(FeatureContext context)
    {
        _windowsKeys = ScreenshotActions.Register(context.Actions, context.Osd, context.Report, context.AskAsync);
        context.Imports.OnReload(DataStores.Screenshots, _windowsKeys.ReloadFromDisk);
    }

    /// The Actions and hotkeys window's "Restore defaults" also puts Windows' own screenshot keys back (<paramref name="name"/>: that window's).
    public IRestorable KeysRestore(string name) => new SettingsRestore<WindowsKeysState>("screenshot-keys", name,
        () => "Puts Windows' own screenshot keys (Print Screen, Win+Shift+S) back as they were.",
        () => new WindowsKeysState(_windowsKeys!.IsOn), () => new WindowsKeysState(false),
        state => { _windowsKeys!.Set(state.Off); return Task.FromResult(true); });
}
