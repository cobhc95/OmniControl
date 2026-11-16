namespace OmniControl.App.Audio;

/// <summary>
/// Brings old mixer.json files up to date (run on every load by <see cref="MixerSettings.Load"/>; a file with none of the old fields
/// passes through unchanged). What was not processed before stays unprocessed: a row whose stored per-row effects mode was Off gets
/// its effects power switched off instead. A program's single output device becomes its "plays on" list. The old fields are cleared,
/// so they are written back empty. A stored sound mode of an older file is simply ignored when the file is read.
/// </summary>
internal static class MixerMigration
{
    // The ids the per-row effects modes were stored under. A program's empty (or missing) mode meant Off; the master's meant Equalizer APO.
    private const string OldOff = "off", OldCapture = "capture", OldVirtual = "virtual";

    /// <summary>Returns true when the file was old and has been converted (the caller saves it).</summary>
    public static bool Run(MixerSettings s)
    {
        if (s.MasterFxMode is null && s.Apps.All(a => a.FxMode is null && a.DeviceId is null)) return false;

        if (s.MasterFxMode == OldOff) s.MasterFxOn = false;
        s.MasterFxMode = null;

        foreach (var app in s.Apps)
        {
            if (app.FxMode is not (OldCapture or OldVirtual)) app.FxOn = false;
            app.FxMode = null;

            if (!string.IsNullOrEmpty(app.DeviceId) && !app.PlaysOn.Contains(app.DeviceId))
            {
                app.PlaysOn.Add(app.DeviceId);
                if (!string.IsNullOrEmpty(app.DeviceName)) s.DeviceNames[app.DeviceId] = app.DeviceName;
            }
            app.DeviceId = null;
            app.DeviceName = null;
        }
        return true;
    }
}
