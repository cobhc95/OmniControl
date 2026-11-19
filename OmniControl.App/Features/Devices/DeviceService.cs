using System.Net;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Devices;

/// What a device is told to do.
public enum DevicePower { On, Off, Toggle }

/// The smart devices: the saved list, their master actions (<c>DeviceService.Actions.cs</c>), their methods and the list's edits
/// (<c>DeviceService.Methods.cs</c>) and the commands themselves, tried method by method in the device's order (<c>DeviceService.Run.cs</c>;
/// scripts: <see cref="ScriptMethod"/>; simulation: <see cref="SimulationMethod"/>; reading on its own: <see cref="DeviceAutoChecks"/>). A burst of commands is cut by the shared
/// <see cref="LaunchLimiter"/>, and a device that is still being told something is not told again.
public sealed partial class DeviceService
{
    public const string Category = "Smart Devices";
    public const string LogFile = "devices-log.txt";

    private readonly IActionRegistry _hotkeys;
    private readonly IStateRegistry _states;
    private readonly ICredentialStore _credentials;
    private readonly Action<string> _report;
    private readonly Action<Osd.OsdMessage> _note;
    private readonly LaunchLimiter _limiter = new();
    private readonly Dictionary<string, DeviceRuntime> _runtime = new();
    private readonly DispatcherTimer _saveSoon = new() { Interval = Timing.DeviceNameSaveDelay };

    public DeviceSettings Settings { get; }

    /// The devices in the order the page shows them (read-only: change them through this service's methods).
    public IReadOnlyList<DeviceRecord> Records => Settings.Devices;

    /// Saved templates for a method's script (read-only).
    public IReadOnlyList<ScriptProfile> Profiles => Settings.Profiles;

    /// Whether auto-check settings are shared by every device.
    public bool AutoCheckSame => Settings.AutoCheckSame;

    public int IndexOf(DeviceRecord device) => Settings.Devices.IndexOf(device);

    public bool Has(DeviceRecord device) => Settings.Devices.Contains(device);
    public DeviceSetup Setup { get; }
    public SimulationSetup Simulation { get; }

    /// The scripts method: running templates and the user's own templates.
    public ScriptMethod Scripts { get; }

    /// The simulation method: running, testing, teaching and recording in the emulator.
    public SimulationMethod SimulationMethod { get; }

    /// When devices read their state on their own.
    public DeviceAutoChecks AutoChecking { get; }

    /// The list changed (a device was added, removed, merged or moved).
    public event Action? ListChanged;

    /// <param name="states">The master list of states: each device's power is registered there.</param>
    /// <param name="note">Where a device's state change or failure is announced (the master OSD).</param>
    /// <param name="hardware">The master list of hardware watches: the service registers the devices' state reading there (after sleep or Remote Desktop the network may be back).</param>
    public DeviceService(IActionRegistry hotkeys, IStateRegistry states, IHardwareWatches hardware, ICredentialStore credentials, Action<string> report, Action<Osd.OsdMessage> note, DeviceSettings? settings = null)
    {
        hardware.Register(new HardwareWatch(HardwareWatchId, "Smart devices", HardwareSignal.Session | HardwareSignal.Resume,
            () => Dispatcher.UIThread.Post(() => _ = Guard.RunAsync("Smart devices: check after a hardware change", AutoChecking.CheckAllNowAsync))));
        _hotkeys = hotkeys;
        _states = states;
        _credentials = credentials;
        _report = report;
        _note = note;
        Settings = settings ?? DeviceSettings.Load();
        Setup = new DeviceSetup(Log);
        Simulation = new SimulationSetup(Log, Settings.Emulator, Edited);
        Scripts = new ScriptMethod(Settings, Setup, credentials, Edited);
        SimulationMethod = new SimulationMethod(Simulation, RuntimeOf, Edited);
        AutoChecking = new DeviceAutoChecks(Settings, Setup, Edited, CheckAutoAsync);
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            Settings.Save();
        };
        if (TakeUp()) Settings.Save();
        RegisterEmulatorState();
        SimulationMethod.WakeAtStart(Settings);
    }

    /// Brings the devices of an older file up to date and registers each one's actions; true when something was changed (the file is saved).
    private bool TakeUp()
    {
        var migrated = false;
        foreach (var device in Settings.Devices)
        {
            migrated |= device.EnsureMethod();
            foreach (var method in device.Methods)
            {
                method.Kind = DeviceMethods.Known(method.Kind);
                migrated |= method.DropOldStateCheck();
            }
            RegisterActions(device);
        }
        return migrated;
    }

    /// Takes devices.json from disk as it is now (an import wrote it): the devices, templates and auto-check choices are replaced, each device's
    /// actions and state are registered again (their keys come back by action id) and the page's list is told. Passwords are never in the file.
    public bool ReloadFromDisk()
    {
        foreach (var device in Settings.Devices.ToList())
        {
            UnregisterActions(device);
            _runtime.Remove(device.Id);
        }
        SettingsCopy.Into(Settings, DeviceSettings.Load());
        if (TakeUp()) Settings.Save();
        ListChanged?.Invoke();
        return true;
    }

    /// The Credential Manager entry that holds a method's password (by the method's id, so a merged method keeps it).
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

    /// Removes the device with its actions (and so their shortcuts), its passwords and its own icon picture.
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

    /// A new name (every key typed): the actions are made again at once, so Shortcuts, Rules and the action picker show it; their shortcuts and
    /// uses follow the ids, so they are kept. The file is saved <see cref="Timing.DeviceNameSaveDelay"/> after the last key.
    public void Rename(DeviceRecord device, string name)
    {
        if (device.Name == name) return;
        device.Name = name;
        RegisterActions(device);
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    /// A field of the record changed: saved at once.
    public void Edited()
    {
        _saveSoon.Stop();
        Settings.Save();
    }

    public bool HasPassword(MethodRecord method) => _credentials.Read(PasswordTarget(method)) is { Length: > 0 };

    /// Keeps the password in the Windows Credential Manager; false when Windows refused.
    public bool SavePassword(MethodRecord method, string password) => _credentials.Write(PasswordTarget(method), password);

    /// Deletes a method's password, unless a device in the list still holds a method with the same id (a backup merged back in).
    private void ForgetPassword(MethodRecord method)
    {
        if (!Settings.Devices.Any(d => d.Methods.Any(m => m.Id == method.Id))) _credentials.Delete(PasswordTarget(method));
    }

    /// The first thing missing before the method can be used, as a short phrase; null when it can.
    public string? MissingFor(MethodRecord method)
    {
        if (method.IsSimulation) return SimulationMethod.Missing(method);
        if (Setup.PythonPath is null) return "Python is not installed";
        if (!Setup.KasaInstalled) return "python-kasa is not installed";
        if (!IPAddress.TryParse(method.Address.Trim(), out _)) return "the device's address is not set";
        if (method.Email.Trim().Length == 0) return "the account's email is not set";
        return HasPassword(method) ? null : "the account's password is not saved";
    }

    /// The methods On, Off, Toggle and Check try, in order: the ready ones.
    public static IReadOnlyList<MethodRecord> ReadyMethods(DeviceRecord device) => device.Methods.Where(m => m.IsReady).ToList();

    /// Why the device cannot be told anything now, as a short phrase; null when at least one ready method can be used.
    public string? MissingFor(DeviceRecord device)
    {
        var ready = ReadyMethods(device);
        if (ready.Count == 0) return "no method is set up yet";
        return ready.Any(m => MissingFor(m) is null) ? null : MissingFor(ready[0]);
    }

    /// The set-up of a method is finished: the device may use it.
    public void FinishSetup(MethodRecord method)
    {
        if (method.IsSimulation) method.SimVerified = true;
        else method.Verified = true;
        Edited();
        ListChanged?.Invoke();
    }

    /// The set-up's Cancel: the record goes back to how it was when the set-up opened (its actions are made again); the password of a method
    /// that was added since is deleted.
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

    internal static string DisplayName(DeviceRecord device) => device.Name.Length > 0 ? device.Name : "A device";

    private string NextName()
    {
        var number = Settings.Devices.Count + 1;
        while (Settings.Devices.Any(d => d.Name == $"Plug {number}")) number++;
        return $"Plug {number}";
    }

    internal static void Log(string line) => AppDataStore.AppendLog(LogFile, line);
}
