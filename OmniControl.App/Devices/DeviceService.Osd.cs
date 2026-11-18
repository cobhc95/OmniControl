using OmniControl.App.Osd;

namespace OmniControl.App.Devices;

// What the master OSD shows for a device (docs/mockups/smart-device-osd.html): its name under the icon picked for it, then "On" or "Off" once a
// command or a read has really set its state, or "Couldn't turn on/off" in red when a command failed. The OSD's "Smart devices" switch decides
// whether anything shows; this only says what happened.
public sealed partial class DeviceService
{
    /// <summary>A command succeeded or a read found a different state: the device now is <paramref name="on"/>.</summary>
    private void NoteState(DeviceRecord device, bool on) =>
        _note(new OsdMessage(OsdIcon.Plug, DisplayName(device), on ? "On" : "Off", LibraryIcon: IconOf(device), Tone: on ? OsdTone.On : OsdTone.Off));

    /// <summary>A command failed (a stop by the user is not a failure).</summary>
    private void NoteFailed(DeviceRecord device, DevicePower power) =>
        _note(new OsdMessage(OsdIcon.Plug, DisplayName(device), power switch
        {
            DevicePower.On => "Couldn't turn on",
            DevicePower.Off => "Couldn't turn off",
            _ => "Couldn't switch",
        }, LibraryIcon: Icons.IconLibrary.Warning, Tone: OsdTone.Failed));
}
