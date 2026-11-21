namespace OmniControl.App.Features.Volume.Pages;

/// Where the level actions put what they just set or read (UI thread); <see cref="VolumeControls"/> is the one, and the page's rows follow it.
internal interface IVolumeLevels
{
    /// The master's level and mute.
    void ShowMaster(int level, bool muted);

    /// A recording device's level and mute (its device id).
    void ShowInput(string deviceId, int level, bool muted);

    /// A combined microphone's level and mute (its key).
    void ShowCombo(string key, int level, bool muted);
}
