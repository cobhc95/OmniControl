using System.Diagnostics;
using System.Net;
using OmniControl.App.Hotkeys;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Devices;

/// <summary>What a device is told to do.</summary>
public enum DevicePower { On, Off, Toggle }

/// <summary>
/// The smart plugs: the saved list, their master actions ("name: On", "Off", "Toggle" in <see cref="HotkeyRegistry"/>) and the commands
/// themselves. A command runs kasa (python-kasa's command line) as the normal user, hidden, stopped after <see cref="Timing.DeviceCommandTimeout"/>;
/// the account reaches it in environment variables, never in the arguments or the log. A burst of commands is cut by the shared
/// <see cref="LaunchLimiter"/>, and a plug that is still being told something is not told again.
/// </summary>
public sealed partial class DeviceService
{
    public const string Category = "Smart Devices";
    public const string LogFile = "devices-log.txt";

    private readonly HotkeyRegistry _hotkeys;
    private readonly ICredentialStore _credentials;
    private readonly Action<string> _report;
    private readonly LaunchLimiter _limiter = new();
    private readonly Dictionary<string, DeviceRuntime> _runtime = new();

    public DeviceSettings Settings { get; }
    public DeviceSetup Setup { get; }
    public SimulationSetup Simulation { get; }

    /// <summary>The list changed (a device was added or removed).</summary>
    public event Action? ListChanged;

    public DeviceService(HotkeyRegistry hotkeys, ICredentialStore credentials, Action<string> report, DeviceSettings? settings = null)
    {
        _hotkeys = hotkeys;
        _credentials = credentials;
        _report = report;
        Settings = settings ?? DeviceSettings.Load();
        Setup = new DeviceSetup(Log);
        Simulation = new SimulationSetup(Log);
        foreach (var device in Settings.Devices)
        {
            device.Method = DeviceMethods.Known(device.Method);
            _hotkeys.Register(MakeActions(device));
        }
    }

    /// <summary>The Credential Manager entry that holds a device's password.</summary>
    public static string PasswordTarget(DeviceRecord device) => $"OmniControl/device/{device.Id}";

    public static string LogPath => AppDataStore.PathOf(LogFile);

    public DeviceRuntime RuntimeOf(DeviceRecord device)
    {
        if (!_runtime.TryGetValue(device.Id, out var runtime)) _runtime[device.Id] = runtime = new DeviceRuntime();
        return runtime;
    }

    public DeviceRecord Add()
    {
        var device = new DeviceRecord { Name = NextName() };
        Settings.Devices.Add(device);
        _hotkeys.Register(MakeActions(device));
        Settings.Save();
        ListChanged?.Invoke();
        return device;
    }

    public void Remove(DeviceRecord device)
    {
        Settings.Devices.Remove(device);
        _runtime.Remove(device.Id);
        _credentials.Delete(PasswordTarget(device));
        foreach (var id in ActionIds(device)) _hotkeys.Unregister(id);
        Settings.Save();
        ListChanged?.Invoke();
    }

    /// <summary>A new name makes the actions again; their shortcuts and uses follow the ids, so they are kept.</summary>
    public void Rename(DeviceRecord device, string name)
    {
        if (device.Name == name) return;
        device.Name = name;
        foreach (var id in ActionIds(device)) _hotkeys.Unregister(id);
        _hotkeys.Register(MakeActions(device));
        Settings.Save();
    }

    /// <summary>A field of the record changed: saved at once.</summary>
    public void Edited() => Settings.Save();

    public bool HasPassword(DeviceRecord device) => _credentials.Read(PasswordTarget(device)) is { Length: > 0 };

    /// <summary>Keeps the password in the Windows Credential Manager; false when Windows refused.</summary>
    public bool SavePassword(DeviceRecord device, string password) => _credentials.Write(PasswordTarget(device), password);

    /// <summary>The first thing missing before the device can be told anything, as a short phrase; null when it is ready.</summary>
    public string? MissingFor(DeviceRecord device)
    {
        if (device.Method == DeviceMethods.Simulation) return SimulationMissing(device);
        if (Setup.PythonPath is null) return "Python is not installed";
        if (!Setup.KasaInstalled) return "python-kasa is not installed";
        if (!IPAddress.TryParse(device.Address.Trim(), out _)) return "the plug's address is not set";
        if (device.Email.Trim().Length == 0) return "the account's email is not set";
        return HasPassword(device) ? null : "the account's password is not saved";
    }

    /// <summary>Tells the device to switch (by its method); the result is also in <see cref="DeviceRuntime"/> and the log. Null when it worked, otherwise one plain sentence.</summary>
    public async Task<string?> RunAsync(DeviceRecord device, DevicePower power)
    {
        var runtime = RuntimeOf(device);
        var name = DisplayName(device);
        if (runtime.Busy) return Fail(runtime, name, "it is still busy with the last command");
        if (MissingFor(device) is { } missing) return Fail(runtime, name, $"{missing}.");
        if (!_limiter.TryAcquire()) return Fail(runtime, name, "too many commands in a short time, so this one was skipped.");
        runtime.Busy = true;
        try
        {
            return device.Method == DeviceMethods.Simulation ? await RunSimulationAsync(device, power, runtime, name) : await RunScriptAsync(device, power, runtime, name);
        }
        finally
        {
            runtime.Busy = false;
        }
    }

    private async Task<string?> RunScriptAsync(DeviceRecord device, DevicePower power, DeviceRuntime runtime, string name)
    {
        var turnOn = power == DevicePower.On || (power == DevicePower.Toggle && runtime.IsOn != true);
        var (file, lead) = Setup.Kasa!.Value;
        var arguments = lead.Concat(KasaCommand.Arguments(device.Address.Trim(), turnOn ? KasaCommand.PowerOn : KasaCommand.PowerOff)).ToList();
        var environment = new Dictionary<string, string>
        {
            [KasaCommand.UserVariable] = device.Email.Trim(),
            [KasaCommand.PasswordVariable] = _credentials.Read(PasswordTarget(device)) ?? string.Empty,
        };
        Log($"\"{name}\" turns {(turnOn ? "on" : "off")} ({device.Address.Trim()})");
        var clock = Stopwatch.StartNew();
        var problem = await ScriptRunner.RunAsync(file, arguments, hidden: true, Timing.DeviceCommandTimeout, environment);
        clock.Stop();
        if (problem is not null) return Fail(runtime, name, problem);

        runtime.Finished(turnOn, $"Last command {DateTime.Now:t} · turned {(turnOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s", failed: false);
        Log($"\"{name}\" turned {(turnOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s");
        return null;
    }

    /// <summary>Turns the plug off, then on again; a plug that does both is marked as tested. Null when it worked, otherwise one plain sentence.</summary>
    public async Task<string?> TestAsync(DeviceRecord device)
    {
        var problem = await RunAsync(device, DevicePower.Off) ?? await RunAsync(device, DevicePower.On);
        if (problem is not null) return problem;
        if (device.Method == DeviceMethods.Simulation) device.SimVerified = true; else device.Verified = true;
        Edited();
        return null;
    }

    private string Fail(DeviceRuntime runtime, string name, string problem, int failedStep = -1, string failedReason = "")
    {
        var sentence = $"{name} didn't switch: {problem}";
        runtime.Finished(runtime.IsOn, $"Last command {DateTime.Now:t} · failed: {problem}", failed: true, failedStep, failedReason);
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

    // ---- The master actions ----

    private static IEnumerable<string> ActionIds(DeviceRecord device) =>
        Enum.GetValues<DevicePower>().Select(power => ActionId(device, power));

    private static string ActionId(DeviceRecord device, DevicePower power) => $"devices.{device.Id}.{power.ToString().ToLowerInvariant()}";

    private IEnumerable<HotkeyAction> MakeActions(DeviceRecord device) =>
        Enum.GetValues<DevicePower>().Select(power =>
            new HotkeyAction(ActionId(device, power), $"{DisplayName(device)}: {power}", Category,
                $"smart plug socket outlet switch power tapo {device.Name}", ids => _ = RunAsync(device, power), group: "Plugs"));
}
