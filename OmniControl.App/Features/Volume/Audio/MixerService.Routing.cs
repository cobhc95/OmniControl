namespace OmniControl.App.Features.Volume.Audio;

/// Where sound goes, as the user set it: each program's and the master's "plays on" devices, combined devices,
/// and the device names remembered for unplugged devices. The mixer stores these; the effects engine (<see cref="Engine.RoutePlanner"/>)
/// decides from them what runs, and tells the mixer each program's own device (<see cref="SetDirectRoute"/>), which the mixer applies
/// to every stream of the program, including ones opened later.
public sealed partial class MixerService
{
    private readonly Dictionary<string, string?> _direct = new();

    /// "Plays on" devices or combined devices changed (UI thread): the engine plans again.
    public event Action? RoutingChanged;

    // ---- "plays on" ----

    /// Where a program plays (empty = it follows the master).
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

    /// How the program's effects run (<see cref="EffectsRules.ProgramRoutes"/>).
    public EffectsRoute FxRouteOf(string key) => EffectsRules.RouteOf(Find(key)?.FxRoute);

    public void SetFxRoute(string key, EffectsRoute route)
    {
        if (Find(key) is not { } e || e.FxRoute == route.Id) return;
        e.FxRoute = route.Id;
        Changed();
        ProgramChanged?.Invoke(key);
    }

    /// How the master runs (<see cref="EffectsRules.MasterRoutes"/>).
    public EffectsRoute MasterMethod => EffectsRules.MasterRouteOf(Settings.MasterMethod);

    /// The master's method: applies at once (the engine plans again on <see cref="RoutingChanged"/>).
    public void SetMasterMethod(EffectsRoute route)
    {
        if (Settings.MasterMethod == route.Id) return;
        Settings.MasterMethod = route.Id;
        Changed();
    }

    /// How a microphone's effects run (<see cref="EffectsRules.MicRoutes"/>); <paramref name="key"/> = its input key.
    public EffectsRoute MicMethodOf(string key) => EffectsRules.MicRouteOf(InputEntry(key).Method);

    public void SetMicMethod(string key, EffectsRoute route)
    {
        var entry = InputEntry(key);
        if (entry.Method == route.Id) return;
        entry.Method = route.Id;
        Changed();
    }

    /// The row's method is Off (key null = the master; a combined device has no Off): nothing is routed or processed for it.
    public bool IsMethodOff(string? key) =>
        (key is null ? MasterMethod : IsInput(key) ? MicMethodOf(key) : CombinedOf(key) is not null ? null : FxRouteOf(key)) == EffectsRules.MethodOff;

    /// Effects were added to a row whose method is Off (key null = the master): its Method menu should open so a method is picked (UI thread).
    public event Action<string?>? MethodWanted;

    /// The master's chosen devices (several, or a combined device); empty = one real device alone (<see cref="MasterDeviceId"/>).
    public IReadOnlyList<string> MasterPlaysOn => Settings.MasterPlaysOn;

    public void SetMasterPlaysOn(IEnumerable<string> targets)
    {
        var list = targets.Distinct().ToList();
        if (list.SequenceEqual(Settings.MasterPlaysOn)) return;
        Settings.MasterPlaysOn = list;
        Changed();
    }

    /// Makes <paramref name="deviceId"/> Windows' default playback device (the master's single device). The user's own choice, like
    /// Windows' Sound settings, so it is not undone at exit. Reports Windows' refusal.
    public void SetDefaultDevice(string deviceId)
    {
        if (!CoreAudio.TrySetDefaultDevice(deviceId, out var error)) Report?.Invoke($"Couldn't make {DeviceNameOf(deviceId)} the default device: {error}");
        ScanAll();
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

    /// Removes a combined device and every "plays on" entry that pointed at it.
    public void RemoveCombined(string key)
    {
        if (Settings.Combined.RemoveAll(c => c.Key == key) == 0) return;
        foreach (var app in Settings.Apps) app.PlaysOn.Remove(key);
        Settings.MasterPlaysOn.Remove(key);
        Changed();
    }

    /// A combined device's new name and members (the Combine devices window's Edit).
    public void EditCombined(string key, string name, IEnumerable<string> members)
    {
        if (CombinedOf(key) is not { } c) return;
        c.Name = name;
        c.Members = members.Distinct().ToList();
        c.DropStaleMemberSettings();
        Changed();
    }

    /// A member added to or taken away from a combined device (the Wiring row's chips, "+ add" and "Add to ..."); the last one stays.
    public void ToggleMember(string key, string member)
    {
        if (CombinedOf(key) is not { } c) return;
        var members = c.Members.ToList();
        if (!members.Remove(member)) members.Add(member);
        if (members.Count == 0) return;
        EditCombined(key, c.Name, members);
    }

    /// A combined microphone member's level (0-200 %) or mute changed (its engine run applies them; no restart).
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

    /// A combined microphone's level or mute changed (its engine run applies them).
    public void SetCombinedLevel(string key, int volume, bool muted)
    {
        if (CombinedOf(key) is not { } c) return;
        (c.Volume, c.Muted) = (Core.Models.Percent.Clamp(volume), muted);
        Settings.Save();
        ProgramChanged?.Invoke(key);
    }

    // ---- names ----

    /// A device's or combined device's name, also for one that is unplugged now (its last known name).
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

    /// The device the program is sent to when no engine holds it (null = Windows' default).
    public string? DirectRouteOf(string key) => _direct.GetValueOrDefault(key);

    /// The engine decided where the program plays when nothing of OmniControl processes it. Applied at once to its streams (unless the
    /// engine holds it on a cable) and to streams it opens later. A program never sent anywhere is never touched.
    public void SetDirectRoute(string key, string? deviceId)
    {
        var had = _direct.TryGetValue(key, out var old);
        if (had && old == deviceId || !had && deviceId is null) return;
        _direct[key] = deviceId;
        if (Holds.IsRoutingHeld(key) || !_sessions.TryGetValue(key, out var list)) return;
        Route(key, list.Select(s => s.ProcessId), deviceId);
    }

    /// A program just opened: send it to its own device, if it has one.
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
