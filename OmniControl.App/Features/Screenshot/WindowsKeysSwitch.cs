namespace OmniControl.App.Features.Screenshot;

/// The switch as "Restore defaults" copies and puts it back (off is the default).
public sealed record WindowsKeysState(bool Off);

/// The "Turn off Windows' own screenshot keys" switch: the one owner of its state. Turning it on
/// remembers what Windows had (<see cref="ScreenshotSettings.WindowsKeysBackup"/>) and turns Print Screen and Win+Shift+S off; turning it off
/// puts those values back. The registry itself is <see cref="SnippingKeys"/> in Core.
public sealed class WindowsKeysSwitch
{
    public const string ActionId = "screenshot.windowskeys";

    private readonly ScreenshotSettings _settings;
    private readonly Action<string> _report;

    public WindowsKeysSwitch(ScreenshotSettings settings, Action<string> report) => (_settings, _report) = (settings, report);

    public bool IsOn => _settings.TurnOffWindowsKeys;

    /// The caution tag while the last Win+Shift+S change has not applied yet (until Explorer restarts or you sign in again); null otherwise.
    public string? PendingTag => _settings.WindowsKeysChangedUtc is { } changed && !SnippingKeys.ExplorerStartedSince(changed) ? "applies after sign-out" : null;

    /// Start-up: Windows may have put the keys back (a reset, an update), so a switch that is on is made true again.
    public void Reapply()
    {
        if (IsOn && !SnippingKeys.IsOff(SnippingKeys.Capture())) SnippingKeys.TurnOff();
    }

    /// Takes screenshots.json from disk as it is now (an import wrote it; this PC's own switch, its backup of Windows' keys and its recording way are
    /// kept by the import) and makes a switch that is on true again. The screenshot actions and the capture service hold this same settings object.
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(_settings, ScreenshotSettings.Load());
        Reapply();
        return true;
    }

    /// The Screenshot tool was switched off: Windows' own keys are put back while the switch stays on, so <see cref="Reapply"/> turns them off
    /// again when the tool is back. Says one line when Windows refused.
    public void GiveBack()
    {
        if (IsOn && _settings.WindowsKeysBackup is { } before && !SnippingKeys.Restore(before)) _report("Couldn't put Windows' screenshot keys back.");
    }

    public void Toggle() => Set(!IsOn);

    /// Turns the switch on or off at once and says in one line when something could not be done or only applies later.
    public void Set(bool on)
    {
        if (on == IsOn) return;
        if (on)
        {
            var before = SnippingKeys.Capture();
            if (before is null || !SnippingKeys.TurnOff())
            {
                _report("Couldn't turn off Windows' screenshot keys.");
                return;
            }
            _settings.WindowsKeysBackup = before;
            _settings.TurnOffWindowsKeys = true;
            _report("Print Screen is off now; Win+Shift+S applies after you sign out.");
        }
        else
        {
            if (_settings.WindowsKeysBackup is { } before && !SnippingKeys.Restore(before))
            {
                _report("Couldn't put Windows' screenshot keys back.");
                return;
            }
            _settings.WindowsKeysBackup = null;
            _settings.TurnOffWindowsKeys = false;
            _report("Windows' screenshot keys are back; Win+Shift+S applies after you sign out.");
        }
        _settings.WindowsKeysChangedUtc = DateTime.UtcNow;
        _settings.Save();
    }
}
