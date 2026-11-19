namespace OmniControl.App.Features.Osd;

/// What a feature that shows pop-ups needs of the OSD (<see cref="OsdService"/> is the one; its own page edits the rest).
public interface IOsd
{
    /// Windows reported a new level or mute of the watched playback or recording device (level, muted), on a Windows audio thread.
    event Action<int, bool>? VolumeWatched, MicrophoneWatched;

    /// Declares a level pop-up a feature shows (once, by that feature), so it gets a "Show in OSD" switch.
    void Declare(string name, OsdLevelIcon icon);

    /// Declares a text pop-up (no level, no icon).
    void DeclareText(string name);

    /// OmniControl itself changed <paramref name="element"/> (any thread), so Windows' echo of it does not pop up again.
    void NoteOwnChange(string element);

    /// Shows the master volume (marked as muted when it is).
    void ShowVolume(int value, bool muted);

    /// Shows <paramref name="element"/> at <paramref name="value"/> (0-100) unless it is switched off. Any thread.
    void Show(string element, int value);

    /// As <see cref="Show(string, int)"/>, with a label other than the element's name.
    void Show(string element, int value, string label);

    /// Shows a text pop-up for a declared text element unless it is switched off. UI thread.
    void ShowMessage(string element, OsdMessage message, string? program = null);

    /// Shows a smart device's state or failure from any thread.
    void ShowDeviceNote(OsdMessage message);

    /// The volume pop-up for outside changes watches the device <paramref name="device"/> names (null = Windows' default playback device).
    void WatchVolumeOf(Func<string?> device);

    /// The watched device's answer changed: the volume pop-up looks again.
    void RetargetVolume();
}
