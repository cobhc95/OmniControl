using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Screenshot;

/// The screenshot actions, and Windows' own screenshot keys that their settings can turn off.
public sealed class ScreenshotModule : IFeatureModule
{
    private WindowsKeysSwitch? _windowsKeys;
    private ScreenshotService? _service;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Screenshot };

    public void Register(FeatureContext context)
    {
        var (windowsKeys, service) = (_windowsKeys, _service) = ScreenshotActions.Register(context.Actions, context.Osd, context.Report, context.AskAsync);
        context.Imports.OnReload(DataStores.Screenshots, windowsKeys.ReloadFromDisk);
        Guard.OffUi("Screen recording: recordings left unsaved", RecordingStage.CleanUp);
        // Switched off: a recording ends and Windows' own keys work again (the switch is kept and turns them off when the feature is back).
        context.Shutdown.AddWhenOff(ShutdownStage.AppWindows, "screenshot tool", () =>
        {
            if (service.IsRecording) service.StopRecording();
            windowsKeys.GiveBack();
            (_windowsKeys, _service) = (null, null);
        });
    }

    /// A screen recording (with its sound) runs now.
    public bool Records => _service?.IsRecording == true;

    /// The Actions and Hotkeys window's "Restore defaults" also puts Windows' own screenshot keys back (<paramref name="name"/>: that window's);
    /// while the Screenshot tool is off there is nothing of it to restore.
    public IRestorable KeysRestore(string name) => new SettingsRestore<WindowsKeysState>("screenshot-keys", name,
        () => "Puts Windows' own screenshot keys (Print Screen, Win+Shift+S) back as they were.",
        () => new WindowsKeysState(_windowsKeys?.IsOn == true), () => new WindowsKeysState(false),
        state => { _windowsKeys?.Set(state.Off); return Task.FromResult(true); });
}
