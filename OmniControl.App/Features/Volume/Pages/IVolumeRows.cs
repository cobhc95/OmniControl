namespace OmniControl.App.Features.Volume.Pages;

/// What the Volume page's level actions and states read and set of its rows; <see cref="VolumePageViewModel"/> is the one (UI thread).
internal interface IVolumeRows
{
    /// The master's row, kept in step with Windows by every scan.
    MixerItem Master { get; }

    /// A recording device's row (null: none).
    InputItem? Input(string deviceId);

    /// The default recording device's row (null: none).
    InputItem? DefaultInput();

    /// A combined device's row (null: none).
    VolumeRow? Combo(string key);

    /// Makes a playback device the master's device, as its Plays on menu does.
    void MakeMasterDevice(string deviceId);
}
