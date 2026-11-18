using System.Net;
using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Devices;

/// <summary>What a device is told to do.</summary>
public enum DevicePower { On, Off, Toggle }

/// <summary>
/// The smart devices: the saved list, their master actions (<c>DeviceService.Actions.cs</c>), their methods and the list's edits
/// (<c>DeviceService.Methods.cs</c>) and the commands themselves, tried method by method in the device's order (<c>DeviceService.Run.cs</c>;
/// scripts: <c>DeviceService.Scripts.cs</c>; simulation: <c>DeviceService.Simulation.cs</c>). A burst of commands is cut by the shared
/// <see cref="LaunchLimiter"/>, and a device that is still being told something is not told again.
/// </summary>
public sealed partial class DeviceService
{
    public const string Category = "Smart Devices";
    public const string LogFile = "devices-log.txt";

    private readonly HotkeyRegistry _hotkeys;
    private readonly StateRegistry _states;
    private readonly ICredentialStore _credentials;
    private readonly Action<string> _report;
    private readonly Action<Osd.OsdMessage> _note;
    private readonly LaunchLimiter _limiter = new();
    private readonly Dictionary<string, DeviceRuntime> _runtime = new();
    private readonly DispatcherTimer _saveSoon = new() { Interval = Timing.DeviceNameSaveDelay };

    public DeviceSettings Settings { get; }

    /// <summary>The devices in the order the page shows them (read-only: change them through this service's methods).</summary>
    public IReadOnlyList<DeviceRecord> Records => Settings.Devices;

    /// <summary>Saved templates for a method's script (read-only).</summary>
    public IReadOnlyList<ScriptProfile> Profiles => Settings.Profiles;

    /// <summary>Whether auto-check settings are shared by every device.</summary>
    public bool AutoCheckSame => Settings.AutoCheckSame;

    public int IndexOf(DeviceRecord device) => Settings.Devices.IndexOf(device);

    public bool Has(DeviceRecord device) => Settings.Devices.Contains(device);
    public DeviceSetup Setup { get; }
    public SimulationSetup Simulation { get; }

    /// <summary>The list changed (a device was added, removed, merged or moved).</summary>
    public event Action? ListChanged;

    /// <param name="states">The master list of states: each device's power is registered there.</param>
    /// <param name="note">Where a device's state change or failure is announced (the master OSD).</param>
    /// <param name="hardware">The master list of hardware watches: the service registers the devices' state reading there (after sleep or Remote Desktop the network may be back).</param>
    public DeviceService(HotkeyRegistry hotkeys, StateRegistry states, HardwareWatches hardware, ICredentialStore credentials, Action<string> report, Action<Osd.OsdMessage> note, DeviceSettings? settings = null)
    {
        hardware.Register(new HardwareWatch(HardwareWatchId, "Smart devices", HardwareSignal.Session | HardwareSignal.Resume,
            () => Dispatcher.UIThread.Post(() => _ = Guard.RunAsync("Smart devices: check after a hardware change", CheckAllNowAsync))));
        _hotkeys = hotkeys;
        _states = states;
        _credentials = credentials;
        _report = report;
        _note = note;
        Settings = settings ?? DeviceSettings.Load();
        Setup = new DeviceSetup(Log);
        Simulation = new SimulationSetup(Log, Settings.Emulator, Edited);
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            Settings.Save();
        };
        var migrated = false;
        foreach (var device in Settings.Devices)
        {
            migrated |= device.Migrate();
            foreach (var method in device.Methods)
            {
                method.Kind = DeviceMethods.Known(method.Kind);
                migrated |= method.DropOldStateCheck();
            }
            RegisterActions(device);
        }
        migrated |= AutoChecks.Migrate(Settings);
        if (migrated) Settings.Save();
        RegisterEmulatorState();
        WakeEmulatorAtStart();
    }

    /// <summary>The Credential Manager entry that holds a method's password (by the method's id, so a merged method keeps it).</summary>
    public static string PasswordTarget(MethodRecord method) => $"OmniControl/device/{method.Id}";

    public static string LogPath => AppDataStore.PathOf(LogFile);

    public DeviceRuntime RuntimeOf(DeviceRecord device)
    {
        if (_runtime.TryGetValue(device.Id, out var runtime)) return runtime;
        _runtime[device.Id] = runtime = new DeviceRuntime();
        Watch(device, runtime);
        return runtime;
    }

    public DeviceRecord Add()
    {
        var device = new DeviceRecord { Name = NextName(), Methods = { new MethodRecord() } };
        Settings.Devices.Add(device);
        RegisterActions(device);
        Settings.Save();
        ListChanged?.Invoke();
        return device;
    }

    /// <summary>Removes the device with its actions (and so their shortcuts), its passwords and its own icon picture.</summary>
    public void Remove(DeviceRecord device)
    {
        ClearShortcuts(device);
        UnregisterActions(device);
        Settings.Devices.Remove(device);
        _runtime.Remove(device.Id);
        foreach (var method in device.Methods) ForgetPassword(method);
        DeleteOwnImage(device);
        Settings.Save();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// A new name (every key typed): the actions are made again at once, so Shortcuts, Rules and the action picker show it; their shortcuts and
    /// uses follow the ids, so they are kept. The file is saved <see cref="Timing.DeviceNameSaveDelay"/> after the last key.
    /// </summary>
    public void Rename(DeviceRecord device, string name)
    {
        if (device.Name == name) return;
        device.Name = name;
        RegisterActions(device);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// <summary>A field of the record changed: saved at once.</summary>
    public void Edited()
    {
        _saveSoon.Stop();
        Settings.Save();
    }

    public bool HasPassword(MethodRecord method) => _credentials.Read(PasswordTarget(method)) is { Length: > 0 };

    /// <summary>Keeps the password in the Windows Credential Manager; false when Windows refused.</summary>
    public bool SavePassword(MethodRecord method, string password) => _credentials.Write(PasswordTarget(method), password);

    /// <summary>Deletes a method's password, unless a device in the list still holds a method with the same id (a backup merged back in).</summary>
    private void ForgetPassword(MethodRecord method)
    {
        if (!Settings.Devices.Any(d => d.Methods.Any(m => m.Id == method.Id))) _credentials.Delete(PasswordTarget(method));
    }

    /// <summary>The first thing missing before the method can be used, as a short phrase; null when it can.</summary>
    public string? MissingFor(MethodRecord method)
    {
        if (method.IsSimulation) return SimulationMissing(method);
        if (Setup.PythonPath is null) return "Python is not installed";
        if (!Setup.KasaInstalled) return "python-kasa is not installed";
        if (!IPAddress.TryParse(method.Address.Trim(), out _)) return "the device's address is not set";
        if (method.Email.Trim().Length == 0) return "the account's email is not set";
        return HasPassword(method) ? null : "the account's password is not saved";
    }

    /// <summary>The methods On, Off, Toggle and Check try, in order: the ready ones.</summary>
    public static IReadOnlyList<MethodRecord> ReadyMethods(DeviceRecord device) => device.Methods.Where(m => m.IsReady).ToList();

    /// <summary>Why the device cannot be told anything now, as a short phrase; null when at least one ready method can be used.</summary>
    public string? MissingFor(DeviceRecord device)
    {
        var ready = ReadyMethods(device);
        if (ready.Count == 0) return "no method is set up yet";
        return ready.Any(m => MissingFor(m) is null) ? null : MissingFor(ready[0]);
    }

    /// <summary>The set-up of a method is finished: the device may use it.</summary>
    public void FinishSetup(MethodRecord method)
    {
        if (method.IsSimulation) method.SimVerified = true;
        else method.Verified = true;
        Edited();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// The set-up's Cancel: the record goes back to how it was when the set-up opened (its actions are made again); the password of a method
    /// that was added since is deleted.
    /// </summary>
    public void Restore(DeviceRecord device, DeviceRecord before)
    {
        var added = device.Methods.Where(m => before.Methods.All(b => b.Id != m.Id)).ToList();
        UnregisterActions(device);
        device.CopyFrom(before);
        foreach (var method in added) ForgetPassword(method);
        RegisterActions(device);
        Edited();
    }

    private string Fail(DeviceRuntime runtime, string name, string problem, int failedStep = -1, string failedReason = "", string details = "", string failedMethodId = "")
    {
        var sentence = $"{name} didn't switch: {problem}";
        runtime.Finished(runtime.IsOn, known: false, $"Failed: {problem}", failed: true, failedStep, failedReason, details: details, failedMethodId: failedMethodId);
        Log($"\"{name}\": {problem.ReplaceLineEndings(" | ")}");
        _report($"{sentence} Details are in {LogFile}.");
        return sentence;
    }

    private static string DisplayName(DeviceRecord device) => device.Name.Length > 0 ? device.Name : "A device";

    private string NextName()
    {
        var number = Settings.Devices.Count + 1;
        while (Settings.Devices.Any(d => d.Name == $"Plug {number}")) number++;
        return $"Plug {number}";
    }

    private static void Log(string line) => AppDataStore.AppendLog(LogFile, line);
}
