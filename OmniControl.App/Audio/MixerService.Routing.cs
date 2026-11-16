using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// Where sound goes, as the user set it: each program's and the master's "plays on" devices, combined devices,
/// and the device names remembered for unplugged devices. The mixer stores these; the effects engine (<see cref="Engine.RoutePlanner"/>)
/// decides from them what runs, and tells the mixer each program's own device (<see cref="SetDirectRoute"/>), which the mixer applies
/// to every stream of the program, including ones opened later.
/// </summary>
public sealed partial class MixerService
{
    private readonly Dictionary<string, string?> _direct = new();

    /// <summary>"Plays on" devices or combined devices changed (UI thread): the engine plans again.</summary>
    public event Action? RoutingChanged;

    // ---- "plays on" ----

    /// <summary>Where a program plays (empty = it follows the master).</summary>
    public IReadOnlyList<string> PlaysOnOf(string key) => Find(key)?.PlaysOn ?? (IReadOnlyList<string>)Array.Empty<string>();

    public void SetPlaysOn(string key, IEnumerable<string> targets)
    {
        if (Find(key) is not { } e) return;
        var list = targets.Distinct().ToList();
        if (list.SequenceEqual(e.PlaysOn)) return;
        e.PlaysOn = list;
        Changed();
        ProgramChanged?.Invoke(key);
    }

    /// <summary>How the program's effects run (<see cref="EffectsRules.ProgramRoutes"/>).</summary>
    public EffectsRoute FxRouteOf(string key) => EffectsRules.RouteOf(Find(key)?.FxRoute);

    public void SetFxRoute(string key, EffectsRoute route)
    {
        if (Find(key) is not { } e || e.FxRoute == route.Id) return;
        e.FxRoute = route.Id;
        Changed();
        ProgramChanged?.Invoke(key);
    }

    /// <summary>How the master runs (<see cref="EffectsRules.MasterRoutes"/>).</summary>
    public EffectsRoute MasterMethod => EffectsRules.MasterRouteOf(Settings.MasterMethod);

    /// <summary>The master's method: applies at once (the engine plans again on <see cref="RoutingChanged"/>).</summary>
    public void SetMasterMethod(EffectsRoute route)
    {
        if (Settings.MasterMethod == route.Id) return;
        Settings.MasterMethod = route.Id;
        Changed();
    }

    /// <summary>The master's chosen devices (several, or a combined device); empty = one real device alone (<see cref="MasterDeviceId"/>).</summary>
    public IReadOnlyList<string> MasterPlaysOn => Settings.MasterPlaysOn;

    public void SetMasterPlaysOn(IEnumerable<string> targets)
    {
        var list = targets.Distinct().ToList();
        if (list.SequenceEqual(Settings.MasterPlaysOn)) return;
        Settings.MasterPlaysOn = list;
        Changed();
    }

    /// <summary>
    /// Makes <paramref name="deviceId"/> Windows' default playback device (the master's single device). The user's own choice, like
    /// Windows' Sound settings, so it is not undone at exit. Reports Windows' refusal.
    /// </summary>
    public void SetDefaultDevice(string deviceId)
    {
        if (!CoreAudio.TrySetDefaultDevice(deviceId, out var error)) Report?.Invoke($"Couldn't make {DeviceNameOf(deviceId)} the default device: {error}");
        Scan();
    }

    // ---- combined devices ----

    public IReadOnlyList<CombinedDevice> Combined => Settings.Combined;

    public CombinedDevice? CombinedOf(string? key) =>
        CombinedDevice.IsKey(key) ? Settings.Combined.FirstOrDefault(c => c.Key == key) : null;

    public void AddCombined(CombinedDevice device)
    {
        Settings.Combined.Add(device);
        Changed();
    }

    /// <summary>Removes a combined device and every "plays on" entry that pointed at it.</summary>
    public void RemoveCombined(string key)
    {
        if (Settings.Combined.RemoveAll(c => c.Key == key) == 0) return;
        foreach (var app in Settings.Apps) app.PlaysOn.Remove(key);
        Settings.MasterPlaysOn.Remove(key);
        Changed();
    }

    /// <summary>A combined device's new name and members (the Combine devices window's Edit).</summary>
    public void EditCombined(string key, string name, IEnumerable<string> members)
    {
        if (CombinedOf(key) is not { } c) return;
        c.Name = name;
        c.Members = members.Distinct().ToList();
        c.DropStaleMemberSettings();
        Changed();
    }

    /// <summary>A member added to or taken away from a combined device (the Wiring row's chips, "+ add" and "Add to ..."); the last one stays.</summary>
    public void ToggleMember(string key, string member)
    {
        if (CombinedOf(key) is not { } c) return;
        var members = c.Members.ToList();
        if (!members.Remove(member)) members.Add(member);
        if (members.Count == 0) return;
        EditCombined(key, c.Name, members);
    }

    /// <summary>A combined microphone member's level (0-200 %) or mute changed (its engine run applies them; no restart).</summary>
    public void SetMemberLevel(string key, string member, int level, bool muted)
    {
        if (CombinedOf(key) is not { } c || !c.Members.Contains(member)) return;
        level = Core.Models.Percent.ClampBoost(level);
        if (c.LevelOf(member) == level && c.IsMuted(member) == muted) return;
        if (level == CombinedDevice.DefaultMemberLevel) c.MemberLevel.Remove(member);
        else c.MemberLevel[member] = level;
        if (muted) c.MemberMuted.Add(member);
        else c.MemberMuted.Remove(member);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    /// <summary>A combined microphone's level or mute changed (its engine run applies them).</summary>
    public void SetCombinedLevel(string key, int volume, bool muted)
    {
        if (CombinedOf(key) is not { } c) return;
        (c.Volume, c.Muted) = (Core.Models.Percent.Clamp(volume), muted);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    // ---- names ----

    /// <summary>A device's or combined device's name, also for one that is unplugged now (its last known name).</summary>
    public string DeviceNameOf(string target) =>
        CombinedOf(target)?.Name
        ?? Devices.Concat(RecordingDevices).FirstOrDefault(d => d.Id == target)?.Name
        ?? Settings.DeviceNames.GetValueOrDefault(target)
        ?? "Unknown device";

    private void RememberNames()
    {
        var changed = false;
        foreach (var d in Devices.Concat(RecordingDevices).Where(d => Settings.DeviceNames.GetValueOrDefault(d.Id) != d.Name))
        {
            Settings.DeviceNames[d.Id] = d.Name;
            changed = true;
        }
        if (changed) Settings.Save();
    }

    // ---- a program's own device, decided by the engine ----

    /// <summary>The device the program is sent to when no engine holds it (null = Windows' default).</summary>
    public string? DirectRouteOf(string key) => _direct.GetValueOrDefault(key);

    /// <summary>
    /// The engine decided where the program plays when nothing of OmniControl processes it. Applied at once to its streams (unless the
    /// engine holds it on a cable) and to streams it opens later. A program never sent anywhere is never touched.
    /// </summary>
    public void SetDirectRoute(string key, string? deviceId)
    {
        var had = _direct.TryGetValue(key, out var old);
        if (had && old == deviceId || !had && deviceId is null) return;
        _direct[key] = deviceId;
        if (IsRoutingHeld(key) || !_sessions.TryGetValue(key, out var list)) return;
        Route(key, list.Select(s => s.ProcessId), deviceId);
    }

    /// <summary>A program just opened: send it to its own device, if it has one.</summary>
    private void ApplyDirect(string key, int pid)
    {
        if (_direct.GetValueOrDefault(key) is { } device) Route(key, new[] { pid }, device);
    }

    private void Route(string key, IEnumerable<int> processIds, string? deviceId)
    {
        if (!CoreAudio.TrySetProgramDevice(processIds, deviceId, out var error))
            Report?.Invoke($"Couldn't change the device for {Find(key)?.Name}: {error}.");
    }

    private void Changed()
    {
        Settings.Save();
        RoutingChanged?.Invoke();
        Updated?.Invoke();
    }
}
