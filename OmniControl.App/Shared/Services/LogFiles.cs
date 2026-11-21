namespace OmniControl.App.Shared.Services;

/// THE list of OmniControl's log files in its data folder, each written only by its owner through <see cref="AppDataStore.AppendLog"/>
/// (one background writer, rolled over at <see cref="Timing.LogMaxBytes"/>).
public static class LogFiles
{
    public const string Errors = "errors-log.txt", Automation = "automation-log.txt", Devices = "devices-log.txt", Display = "display-log.txt",
        Mouse = "mouse-log.txt", Trackpad = "trackpad-log.txt", Rules = "rules-log.txt", Osd = "osd-log.txt", EffectsSetup = "effects-setup-log.txt",
        Backup = "backup-log.txt", Hardware = "hardware-log.txt", Power = "power-log.txt", Timing = "timing-log.txt", Hang = "hang-log.txt";
}
