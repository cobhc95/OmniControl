using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniControl.App.Features.Devices;

/// One way a device is reached, with its own set-up: scripts (template, address, sign-in) or
/// simulation (emulator, steps, teaching). A device holds these in order and tries them in that order. The password is never part of this
/// record (it lives in the Windows Credential Manager under <see cref="DeviceService.PasswordTarget"/>, by this method's id), so a backup holds no secret.
public sealed class MethodRecord
{
    /// The method's own id: its password and its "via" actions follow it, also when it is merged into another device.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// How OmniControl reaches the device this way (<see cref="DeviceMethods"/>).
    public string Kind { get; set; } = DeviceMethods.Scripts;

    /// The plug's IP address on the home network.
    public string Address { get; set; } = string.Empty;

    /// The Tapo account's email.
    public string Email { get; set; } = string.Empty;

    /// The scripts method's template (<see cref="ScriptProfiles"/>: a built-in id, or one of the user's own); empty = Auto-detect.
    public string ProfileId { get; set; } = ScriptProfiles.Auto;

    /// The ids of the template stages the user confirmed ("I've turned it on") for this method.
    public List<string> StagesDone { get; set; } = new();

    /// The user said they reserved the address in their router, so it will not change.
    public bool Reserved { get; set; }

    /// The scripts method's set-up was finished (Finish setup).
    public bool Verified { get; set; }

    /// The simulation method: the emulator the device's app is in (an <see cref="Emulators"/> id; empty = the first installed one).
    public string EmulatorId { get; set; } = string.Empty;

    /// The simulation method: where adb reaches that emulator (<c>127.0.0.1:5555</c>); empty = the emulator's usual address.
    public string AdbAddress { get; set; } = string.Empty;

    /// The simulation method: the one recording; its last Tap is the device's switch.
    public List<SimStep> Steps { get; set; } = new();

    /// The simulation method: how the device shows on and off in its app (taught in set-up), and how sure recent readings were.
    public StateModel Taught { get; set; } = new();

    /// The simulation method: the user said the device's app is installed and signed in inside the emulator.
    public bool SimAppReady { get; set; }

    /// The simulation method's set-up was finished.
    public bool SimVerified { get; set; }

    [JsonIgnore]
    public bool IsSimulation => Kind == DeviceMethods.Simulation;

    /// The method's set-up is finished: the device may use it.
    [JsonIgnore]
    public bool IsReady => IsSimulation ? SimVerified && Taught.IsTaught : Verified;

    /// A full copy (the set-up's Cancel puts it back).
    public MethodRecord Clone() => JsonSerializer.Deserialize<MethodRecord>(JsonSerializer.Serialize(this))!;

    /// Recordings made before on and off were taught ended with a Check that read the switch and has no words; the teaching replaces it, so
    /// such a closing Check is dropped when the devices are loaded. True when one was.
    public bool DropOldStateCheck()
    {
        if (Steps.Count == 0 || Steps[^1] is not { Kind: SimStepKind.Check, Text.Length: 0, Description.Length: 0 }) return false;
        Steps.RemoveAt(Steps.Count - 1);
        return true;
    }
}
