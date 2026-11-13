using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Where each row plays or records from, read from the one routing model (<see cref="TargetsOf"/>: the mixer's "plays on" lists, the
/// master's devices, combined devices' members), and what a choice in the Wiring tab does to it. The Mixer row's sentence and gates,
/// and the engine's status, are kept in step here too.
/// </summary>
public sealed partial class VolumePageViewModel
{
    /// <summary>The "device" a program has when it follows the master (an empty "plays on" list).</summary>
    private const string DefaultTarget = "";
    private const string FollowsMasterName = "Master";
    private const string NameJoin = " + ";

    /// <summary>The engine's status of a row changed (key null = the master).</summary>
    private void OnEngineStatus(string? key)
    {
        var rowKey = key ?? MixerService.MasterKey;
        if (RowOf(rowKey) is { } row) SyncRow(row);
        SyncWiringOf(rowKey);
    }

    private VolumeRow? RowOf(string key) =>
        key == MixerService.MasterKey ? _master
        : _items.GetValueOrDefault(key) ?? (VolumeRow?)_combos.GetValueOrDefault(key)
        ?? _inputs.Values.FirstOrDefault(i => i.Key == key);

    /// <summary>The mode or what this PC offers changed: the Wiring tab's top bar, the search cards and every row follow.</summary>
    private void SyncModes()
    {
        SyncSoundMode();
        CombineGate.Sync(_modes.Check(SoundFeature.CombineDevices));
        if (CombineGate.Reason is { } reason) _combineCard.Availability.MakeUnavailable(reason);
        else _combineCard.Availability.MakeAvailable();
        ModeIndicator = $"{_mixer.SoundMode.Name} mode";
        CableStatus = _modes.StatusOf(SoundModes.Full).FirstOrDefault();
        var setup = _modes.Current?.Cables.Count == 0 ? CableSetupAction() : null;
        if (setup?.Label != CableSetup?.Label) CableSetup = setup;
        foreach (var row in AllRows()) SyncRow(row);
        SyncWiring();
    }

    /// <summary>A Mixer row's sentence, gates, status and mode line.</summary>
    private void SyncRow(VolumeRow row)
    {
        var status = _engine.StatusOf(row.Key == MixerService.MasterKey ? null : row.Key);
        var mode = _mixer.SoundMode;
        row.Status = status.State == Audio.Engine.EngineState.Off ? string.Empty : status.Text;
        row.ModeLine = status.LineFor(mode, row.ChainKey);
        row.RouteText = RouteTextOf(row);
        switch (row)
        {
            case MixerItem { IsMaster: true } master:
                var targets = MasterTargets();
                master.VolumeTipText = targets.Count > 1 ? $"Volume of {NameOf(targets[0])}; the other devices keep their own level" : "Volume";
                break;
            case MixerItem program:
                program.CanTest = mode == SoundModes.Fast && program.IsOpen && _modes.Current?.ProcessLoopback != false;
                program.CaptureGate.Sync(_modes.ProgramCaptureCaution is { } cannotRecord ? new Audio.Gate(cannotRecord, SoundModes.Full) : Audio.Gate.Open);
                break;
            case InputItem input:
                input.EffectsGate.Sync(_modes.Check(SoundFeature.MicEffects));
                input.Fix = status.State == Audio.Engine.EngineState.Unavailable && mode == SoundModes.Fast ? MicApoFix(input.Id) : null;
                break;
            case ComboItem combo:
                combo.EffectsGate.Sync(_modes.Check(SoundFeature.CombineDevices));
                break;
        }
    }

    // ---- the routing model ----

    /// <summary>
    /// The devices a row plays on or records from, as stored: a program's "plays on" list (empty = it follows the master), the master's
    /// devices, a combined device's members. Both tabs read this; nothing keeps a copy.
    /// </summary>
    private IReadOnlyList<string> TargetsOf(VolumeRow row) => row switch
    {
        MixerItem { IsMaster: true } => MasterTargets(),
        MixerItem program => _mixer.PlaysOnOf(program.Key),
        ComboItem combo => _mixer.CombinedOf(combo.Key)?.Members ?? (IReadOnlyList<string>)Array.Empty<string>(),
        _ => Array.Empty<string>(),
    };

    /// <summary>The Mixer's read-only words: "Speakers + Interface 2", "Master" for a program that follows it.</summary>
    private string RouteTextOf(VolumeRow row)
    {
        var targets = TargetsOf(row);
        return row is MixerItem { IsMaster: false } && targets.Count == 0 ? FollowsMasterName : NamesOf(targets);
    }

    /// <summary>A target's name: the combined device whose cable it is, else the device (also when unplugged).</summary>
    private string NameOf(string target) => _mixer.Combined.FirstOrDefault(c => c.Cable == target && c.Cable.Length > 0)?.Name ?? _mixer.DeviceNameOf(target);

    private string NamesOf(IReadOnlyList<string> targets) => targets.Count == 0 ? "nothing" : string.Join(NameJoin, targets.Select(NameOf));

    private string? DefaultDeviceId => _mixer.Devices.FirstOrDefault(d => d.IsDefault)?.Id;

    /// <summary>Where the master plays: its several devices (Full control), else Windows' default device.</summary>
    private IReadOnlyList<string> MasterTargets() =>
        _mixer.SoundMode == SoundModes.Full && _mixer.MasterPlaysOn.Count > 1 ? _mixer.MasterPlaysOn
        : DefaultDeviceId is { } id ? new[] { id } : Array.Empty<string>();

    // ---- what a choice does ----

    /// <summary>A program's choice: Master clears the list; a device is added or taken away (several allowed), or becomes the only one.</summary>
    private void ChooseProgramTarget(string key, string target, bool several)
    {
        var list = _mixer.PlaysOnOf(key).ToList();
        if (target == DefaultTarget) list.Clear();
        else if (!several) list = new List<string> { target };
        else if (!list.Remove(target)) list.Add(target);
        _mixer.SetPlaysOn(key, list);
    }

    /// <summary>
    /// The master's choice: one device = Windows' default device; several (Full control) = the master bus plays on all of them, with the
    /// first plugged-in device as the default. The last device can't be taken away.
    /// </summary>
    private void ChooseMasterTarget(string target, bool several)
    {
        var list = MasterTargets().ToList();
        if (!several) list = new List<string> { target };
        else if (!list.Remove(target)) list.Add(target);
        if (list.Count == 0) return;
        if (list.Count == 1)
        {
            _mixer.SetMasterPlaysOn(Array.Empty<string>());
            MakeDefault(list[0]);
            return;
        }
        _mixer.SetMasterPlaysOn(list);
        if (DefaultDeviceId is not { } current || !list.Contains(current))
            if (list.FirstOrDefault(t => !CombinedDevice.IsKey(t)) is { } first) MakeDefault(first);
    }

    /// <summary>Windows' default device becomes this device, or a combined device's cable (everything sent there plays on its members).</summary>
    private void MakeDefault(string target)
    {
        if (_mixer.CombinedOf(target) is not { } combo) _mixer.SetDefaultDevice(target);
        else if (combo.Cable.Length > 0) _mixer.SetDefaultDevice(combo.Cable);
        else _report($"{combo.Name} has no virtual cable yet ({SoundModes.NeedsAnotherCable.ToLowerInvariant()}).");
    }
}
