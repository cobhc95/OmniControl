using CommunityToolkit.Mvvm.Input;
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
        SyncCables(); // which cable a run uses may have changed
    }

    private VolumeRow? RowOf(string key) =>
        key == MixerService.MasterKey ? _master
        : _items.GetValueOrDefault(key) ?? (VolumeRow?)_combos.GetValueOrDefault(key)
        ?? _inputs.Values.FirstOrDefault(i => i.Key == key);

    /// <summary>What this PC offers changed: the Wiring tab's top bar, the search cards and every row follow.</summary>
    private void SyncSupport()
    {
        SyncAvailability();
        CableStatus = _support.CableStatus;
        var setup = _support.Current is { } support && !EffectsRules.CableInstalled(support) ? CableSetupAction() : null;
        if (setup?.Label != CableSetup?.Label) CableSetup = setup;
        foreach (var row in AllRows()) SyncRow(row);
        SyncWiring();
        SyncCables();
    }

    /// <summary>
    /// What the plan offers changed (a cable freed or taken, the master's method...): every gate, menu and table that reads it follows at
    /// once, so a choice that became possible is selectable without a refresh.
    /// </summary>
    private void OnPlanChanged()
    {
        SyncAvailability();
        foreach (var row in AllRows()) SyncRow(row);
        SyncWiring();
        SyncCables();
    }

    /// <summary>The gates that depend on what is free: the two "+ Combine" pills and the hotkeys that need something.</summary>
    private void SyncAvailability()
    {
        CombineSpeakersGate.Sync(CreateGateFor(false));
        CombineMicrophonesGate.Sync(CreateGateFor(true));
        SyncHotkeyAvailability();
    }

    /// <summary>
    /// A Mixer row's columns from the live state: where it plays, its Method chip and the fact under it, the long line behind them, the
    /// FX button's glow, its gate and Method menu (the master's menu also holds the install steps), and the one step that fixes what is missing.
    /// </summary>
    private void SyncRow(VolumeRow row)
    {
        var status = _engine.StatusOf(row.Key == MixerService.MasterKey ? null : row.Key);
        // A row whose effects are off says nothing, unless the state carries a fact (the master playing straight on the speakers).
        row.Status = status.State == Audio.Engine.EngineState.Off && status.Kind == StatusKind.Other ? string.Empty : status.Brief;
        row.EngineLine = status.TipFor(_engine.MethodFor(row.ChainKey));
        row.RouteText = RouteTextOf(row);
        row.FxRuns = FxSlot.AnyRuns(row.FxOn, _mixer.FxChain(row.ChainKey));
        var method = EffectsRules.ChipMethodOf(MethodFactsOf(row, row.FxRuns));
        row.Method = MethodChip.Of(method);
        row.Fact = status.Fact(method);
        switch (row)
        {
            case MixerItem { IsMaster: true } master:
                var targets = MasterTargets();
                master.VolumeTipText = targets.Count > 1 && (_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId) is { } volumeDevice
                    ? $"Volume of {NameOf(volumeDevice)}; the other devices keep their own level" : "Volume";
                var fix = MasterApoFix();
                master.PlaysOn?.Gate.Sync(OutputsGateOf(master));
                master.RouteChoice?.Sync(_mixer.MasterMethod, r => EffectsRules.MasterOfferOf(r, _engine.MasterFacts, _engine.MasterMethod), fix);
                master.RouteChoice?.SyncSteps(SetupSteps());
                // A master set to the cable bus whose cable is gone is never left silently without effects: the row says so and offers Equalizer APO.
                master.Fix = status.Kind == StatusKind.ApoNotApplied ? fix
                    : _mixer.MasterMethod == EffectsRules.MasterBus && EffectsRules.IsCableMissing(status.Kind) && master.RouteChoice is { ApoAvailable: true } choose
                        ? choose.UseApo
                        : null;
                master.EngineLine += Environment.NewLine + EffectsRules.MasterCableLine(_engine.MasterMethod, _engine.CableUses);
                break;
            case MixerItem program:
                // Test checks recording a program: it records unless the program is set to its cable.
                program.CanTest = _mixer.FxRouteOf(program.Key) != EffectsRules.RouteCable
                    && program.IsOpen && _support.Current?.ProcessLoopback != false;
                program.PlaysOn?.Gate.Sync(OutputsGateOf(program));
                var route = _mixer.FxRouteOf(program.Key);
                var facts = RouteFactsOf(program.Key);
                program.RouteChoice?.SyncProgram(route, facts, ProgramMethodFix(facts));
                // A program set to Virtual cable that has no cable is never left silently without effects: the row says so and offers Recording.
                program.Fix = program.FxCount > 0 && route == EffectsRules.RouteCable && EffectsRules.IsCableMissing(status.Kind)
                    && program.RouteChoice is { RecordingAvailable: true } choice
                    ? choice.UseRecording : null;
                break;
        }
    }

    /// <summary>What a row's Method chip is worked out from (<see cref="EffectsRules.ChipMethodOf"/>).</summary>
    private RowMethodFacts MethodFactsOf(VolumeRow row, bool runs) => row switch
    {
        MixerItem { IsMaster: true } => new(RowKind.Master, runs, _engine.MethodOf(null), _engine.MasterMethod),
        MixerItem program => new(RowKind.Program, runs, _engine.MethodOf(program.Key), _engine.MasterMethod,
            FollowsMaster: _mixer.PlaysOnOf(program.Key).Count == 0, Route: _mixer.FxRouteOf(program.Key), CableFree: _engine.CablesFor(program.Key).Free),
        InputItem input => new(RowKind.Microphone, runs, _engine.MethodOf(input.Key), _engine.MasterMethod),
        _ => new(RowKind.Combined, runs, _engine.MethodOf(row.Key), _engine.MasterMethod),
    };

    /// <summary>What a program's Method menu needs: the cables the engine has, why recording can't be used here (if so) and how its effects run now.</summary>
    private RouteFacts RouteFactsOf(string key) =>
        new(_engine.CablesFor(key), _support.Facts is { } facts ? EffectsRules.CaptureMissing(facts) : null, _engine.MethodOf(key), _engine.ApoCableWhyFor(key));

    /// <summary>
    /// The one step under a program's Method cards that fixes what a greyed card lacks: install VB-CABLE when none is installed, Choose
    /// devices when Equalizer APO is not ticked on a cable; null when nothing here fixes it.
    /// </summary>
    private CardAction? ProgramMethodFix(RouteFacts facts)
    {
        if (facts.Cables.Pairs == 0 && facts.Cables.NoCable == StatusKind.NeedsCable) return CableSetupAction();
        var apo = _support.Current?.Apo ?? Core.Engine.Apo.ApoState.None;
        return facts.ApoCableWhy == EffectsRules.ApoNotOnCable && EffectsRules.DeviceSelectorFixes(apo)
            ? _cableDevices ??= new CardAction("Choose devices",
                "Opens Equalizer APO's Device Selector: tick the virtual cable on its Playback tab (administrator). A restart may be needed.",
                ChooseDevicesCommand)
            : null;
    }

    // ---- the routing model ----

    /// <summary>
    /// The devices a row plays on or records from, as stored: a program's "plays on" list (empty = it follows the master), the master's
    /// devices, a combined device's members, a microphone's own device. Both tabs read this; nothing keeps a copy.
    /// </summary>
    private IReadOnlyList<string> TargetsOf(VolumeRow row) => row switch
    {
        MixerItem { IsMaster: true } => MasterTargets(),
        MixerItem program => _mixer.PlaysOnOf(program.Key),
        ComboItem combo => _mixer.CombinedOf(combo.Key)?.Members ?? (IReadOnlyList<string>)Array.Empty<string>(),
        InputItem input => new[] { input.Id },
        _ => Array.Empty<string>(),
    };

    /// <summary>The Plays on chip's words: "Speakers + Interface 2", "Master" for a program that follows it, a microphone's own name.</summary>
    private string RouteTextOf(VolumeRow row)
    {
        var targets = TargetsOf(row);
        return row is MixerItem { IsMaster: false } && targets.Count == 0 ? FollowsMasterName : NamesOf(targets);
    }

    /// <summary>A target's name: the combined device whose cable it is, else the device (also when unplugged).</summary>
    private string NameOf(string target) => _mixer.Combined.FirstOrDefault(c => c.Cable == target && c.Cable.Length > 0)?.Name ?? _mixer.DeviceNameOf(target);

    private string NamesOf(IReadOnlyList<string> targets) => targets.Count == 0 ? "nothing" : string.Join(NameJoin, targets.Select(NameOf));

    /// <summary>
    /// Where the master plays: the devices chosen for it while it runs on the cable bus, else its one real device
    /// (<see cref="MixerService.MasterDeviceId"/>, or Windows' default).
    /// </summary>
    private IReadOnlyList<string> MasterTargets() =>
        MasterOnBus && _mixer.MasterPlaysOn.Count > 0 ? _mixer.MasterPlaysOn
        : (_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId) is { } id ? new[] { id } : Array.Empty<string>();

    /// <summary>The master runs on the cable bus (its method): only then can it play on several devices.</summary>
    private bool MasterOnBus => _engine.MasterMethod == RunMethod.Bus;

    /// <summary>The master's devices changed (the real device behind the master bus): its level, tip and Wiring row follow.</summary>
    private void OnMasterDeviceChanged()
    {
        SyncMaster();
        SyncWiringOf(MixerService.MasterKey);
    }

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
    /// The master's choice: one device = the master's real device (the master bus plays on it and its cable stays
    /// the default); several, or a combined device = the master bus plays on all of
    /// them, with the first real device as the master's real device. The last device can't be taken away.
    /// </summary>
    private void ChooseMasterTarget(string target, bool several)
    {
        var list = MasterTargets().ToList();
        if (!several) list = new List<string> { target };
        else if (!list.Remove(target)) list.Add(target);
        if (list.Count == 0) return;
        if (list.Count == 1 && !(MasterOnBus && CombinedDevice.IsKey(list[0])))
        {
            _mixer.SetMasterPlaysOn(Array.Empty<string>());
            MakeMasterDevice(list[0]);
            return;
        }
        _mixer.SetMasterPlaysOn(list);
        if ((_mixer.MasterDeviceId ?? _mixer.DefaultDeviceId) is not { } current || !list.Contains(current))
            if (list.FirstOrDefault(t => !CombinedDevice.IsKey(t)) is { } first) MakeMasterDevice(first);
    }

    /// <summary>The master's real device becomes this device: the bus's output while it owns Windows' default, else Windows' default itself.</summary>
    private void MakeMasterDevice(string target)
    {
        if (_mixer.MasterBusOwnsDefault && !CombinedDevice.IsKey(target)) _mixer.SetRealMasterDevice(target);
        else MakeDefault(target);
    }

    /// <summary>Windows' default device becomes this device, or a combined device's cable (everything sent there plays on its members).</summary>
    private void MakeDefault(string target)
    {
        if (_mixer.CombinedOf(target) is not { } combo) _mixer.SetDefaultDevice(target);
        else if (combo.Cable.Length > 0) _mixer.SetDefaultDevice(combo.Cable);
        else _report($"{combo.Name} has no virtual cable yet ({EffectsRules.NeedsAnotherCable.ToLowerInvariant()}).");
    }
}
