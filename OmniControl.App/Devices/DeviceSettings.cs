using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// One smart plug the user added. Only facts about it are kept here: the password is never part of this record (it lives in the Windows
/// Credential Manager under <see cref="DeviceService.PasswordTarget"/>), so a backup of devices.json holds no secret.
/// </summary>
public sealed class DeviceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>The plug's IP address on the home network.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The Tapo account's email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>How OmniControl reaches it (<see cref="DeviceMethods"/>).</summary>
    public string Method { get; set; } = DeviceMethods.Scripts;

    /// <summary>The user said they reserved the address in their router, so it will not change.</summary>
    public bool Reserved { get; set; }

    /// <summary>A test of this plug worked once, so the set-up checklist is no longer shown.</summary>
    public bool Verified { get; set; }

    /// <summary>The simulation method: the emulator the device's app is in (an <see cref="Emulators"/> id; empty = the first installed one).</summary>
    public string EmulatorId { get; set; } = string.Empty;

    /// <summary>The simulation method: where adb reaches that emulator (<c>127.0.0.1:5555</c>); empty = the emulator's usual address.</summary>
    public string AdbAddress { get; set; } = string.Empty;

    /// <summary>The simulation method: the one recording of this device, ending with the Check step.</summary>
    public List<SimStep> Steps { get; set; } = new();

    /// <summary>A test of the recorded steps worked once (the simulation method's own flag, so each method keeps its own checklist).</summary>
    public bool SimVerified { get; set; }
}

/// <summary>The master list of ways to reach a device; a new one is one more entry here.</summary>
public static class DeviceMethods
{
    public const string Scripts = "Scripts", Simulation = "Simulation";

    public static IReadOnlyList<string> All { get; } = new[] { Scripts, Simulation };

    /// <summary>The method stored in a file, or the scripts method when it names one that is not known.</summary>
    public static string Known(string? stored) => All.Contains(stored ?? string.Empty) ? stored! : Scripts;

    /// <summary>One sentence saying what the method does (the chip's tooltip and the menu's rows).</summary>
    public static string Tip(string method) => method == Simulation
        ? "Simulation: OmniControl taps through the device's own Android app in an emulator, the way you would, with steps you record."
        : "Scripts: OmniControl runs python-kasa's command line to switch the plug.";
}

/// <summary>The saved devices, in %APPDATA%\OmniControl\devices.json. Loading never runs anything.</summary>
public sealed class DeviceSettings
{
    public List<DeviceRecord> Devices { get; set; } = new();

    private static readonly DataStore Store = DataStores.Devices;

    public static DeviceSettings Load() => AppDataStore.Load<DeviceSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
