using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// One way a device is reached (docs/mockups/device-multiple-methods.html), with its own set-up: scripts (template, address, sign-in) or
/// simulation (emulator, steps, teaching). A device holds these in order and tries them in that order. The password is never part of this
/// record (it lives in the Windows Credential Manager under <see cref="DeviceService.PasswordTarget"/>, by this method's id), so a backup holds no secret.
/// </summary>
public sealed class MethodRecord
{
    /// <summary>The method's own id: its password and its "via" actions follow it, also when it is merged into another device.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>How OmniControl reaches the device this way (<see cref="DeviceMethods"/>).</summary>
    public string Kind { get; set; } = DeviceMethods.Scripts;

    /// <summary>The plug's IP address on the home network.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The Tapo account's email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The scripts method's template (<see cref="ScriptProfiles"/>: a built-in id, or one of the user's own); empty = Auto-detect.</summary>
    public string ProfileId { get; set; } = ScriptProfiles.Auto;

    /// <summary>The ids of the template stages the user confirmed ("I've turned it on") for this method.</summary>
    public List<string> StagesDone { get; set; } = new();

    /// <summary>The user said they reserved the address in their router, so it will not change.</summary>
    public bool Reserved { get; set; }

    /// <summary>The scripts method's set-up was finished (Finish setup).</summary>
    public bool Verified { get; set; }

    /// <summary>The simulation method: the emulator the device's app is in (an <see cref="Emulators"/> id; empty = the first installed one).</summary>
    public string EmulatorId { get; set; } = string.Empty;

    /// <summary>The simulation method: where adb reaches that emulator (<c>127.0.0.1:5555</c>); empty = the emulator's usual address.</summary>
    public string AdbAddress { get; set; } = string.Empty;

    /// <summary>The simulation method: the one recording; its last Tap is the device's switch.</summary>
    public List<SimStep> Steps { get; set; } = new();

    /// <summary>The simulation method: how the device shows on and off in its app (taught in set-up), and how sure recent readings were.</summary>
    public StateModel Taught { get; set; } = new();

    /// <summary>The simulation method: the user said the device's app is installed and signed in inside the emulator.</summary>
    public bool SimAppReady { get; set; }

    /// <summary>The simulation method's set-up was finished.</summary>
    public bool SimVerified { get; set; }

    [JsonIgnore]
    public bool IsSimulation => Kind == DeviceMethods.Simulation;

    /// <summary>The method's set-up is finished: the device may use it.</summary>
    [JsonIgnore]
    public bool IsReady => IsSimulation ? SimVerified && Taught.IsTaught : Verified;

    /// <summary>A full copy (the set-up's Cancel puts it back).</summary>
    public MethodRecord Clone() => JsonSerializer.Deserialize<MethodRecord>(JsonSerializer.Serialize(this))!;

    /// <summary>
    /// Recordings made before on and off were taught ended with a Check that read the switch and has no words; the teaching replaces it, so
    /// such a closing Check is dropped when the devices are loaded. True when one was.
    /// </summary>
    public bool DropOldStateCheck()
    {
        if (Steps.Count == 0 || Steps[^1] is not { Kind: SimStepKind.Check, Text.Length: 0, Description.Length: 0 }) return false;
        Steps.RemoveAt(Steps.Count - 1);
        return true;
    }
}
