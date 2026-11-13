namespace OmniControl.App.Audio;

/// <summary>
/// Brings mixer.json files from before the global sound mode up to date (run on every load by <see cref="MixerSettings.Load"/>; a file
/// that already has a sound mode passes through unchanged). Old per-row effects modes become one global mode, and what was not
/// processed before stays unprocessed: a row whose stored mode was Off gets its effects power switched off instead. A program's
/// single output device becomes its "plays on" list. The old fields are cleared, so they are written back empty.
/// </summary>
internal static class MixerMigration
{
    // The ids the per-row effects modes were stored under. A program's empty (or missing) mode meant Off; the master's meant Equalizer APO.
    private const string OldOff = "off", OldCapture = "capture", OldVirtual = "virtual";

    /// <summary>Returns true when the file was old and has been converted (the caller saves it).</summary>
    public static bool Run(MixerSettings s)
    {
        if (s.SoundMode.Length > 0) return false;
        s.SoundMode = (s.Apps.Any(a => a.FxMode == OldVirtual && a.FxOn) ? SoundModes.Full : SoundModes.Fast).Id;

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
