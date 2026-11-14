using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Wiring tab: one table row per source (the master, programs and combined speakers in Outputs; recording devices and combined
/// microphones in Microphones), all made from the Mixer's own rows and the routing model (<see cref="TargetsOf"/>); a cell's menu
/// (what can still be added, dimmed where the mode forbids it with the reason and one Switch; a microphone's "Combine with"); the
/// Cable column with the step that gets a cable; and the jump from a Mixer row.
/// </summary>
public sealed partial class VolumePageViewModel
{
    private const string ThisMicrophone = "This microphone", FollowsMasterNote = "Follows the master";
    private const string FollowMasterChoice = "Master (default device)";
    private const string CableTag = "Cable";
    private const string AddTitle = "Add a device", ChooseTitle = "Choose a device";
    private const string CombineWithTitle = "Combine with", CombineWithLabel = "Combine with...", CombineWithTip = "Mixes this microphone with others into one";
    private const string AddMicTitle = "Add a microphone", AddMicTip = "Add a microphone to the mix";
    private const string AddSpeakerTip = "Add a device that plays the same sound";
    private const string MemberTip = "Its level and mute in the mix. Its own effects are not included.";
    private const string RenameChoice = "Rename...", RenameTip = "Its name and devices in the Combine devices window";

    /// <summary>The master, programs and combined speakers, with "+ Combine speakers" at the right of the header (set in the constructor).</summary>
    public VolumeGroup WiringOutputs { get; }

    /// <summary>Recording devices and combined microphones, with "+ Combine microphones" at the right of the header (set in the constructor).</summary>
    public VolumeGroup WiringInputs { get; }

    private readonly Dictionary<string, WiringRow> _wiring = new();

    /// <summary>A jump from the Mixer wants this Wiring row or card shown (the page moves the keyboard to it and scrolls it into view).</summary>
    public event Action<object>? RevealRequested;

    private VolumeGroup NewWiringOutputs() => new("Volume/Wiring/Outputs", "Outputs")
    {
        ExpandedByDefault = true,
        RouteColumn = "Plays on",
        Tip = "A chip's × removes a device, + add adds one. Dimmed choices need the other sound mode.",
        SummaryBesideName = true, // the header's right end holds "+ Combine speakers"
        ToolGate = CombineSpeakersGate,
        Tool = new ModeCardAction("+ Combine speakers", "Plays the same sound on several speakers (Full control)", CombineSpeakersCommand, Main: true),
    };

    private VolumeGroup NewWiringInputs() => new("Volume/Wiring/Microphones", "Microphones")
    {
        ExpandedByDefault = true,
        RouteColumn = "Records from",
        SummaryBesideName = true, // the header's right end holds "+ Combine microphones"
        ToolGate = CombineMicrophonesGate,
        Tool = new ModeCardAction("+ Combine microphones", "Mixes several microphones into one that apps record from", CombineMicrophonesCommand, Main: true),
    };

    /// <summary>Brings the Wiring tab in line with the Mixer's rows and the routing model.</summary>
    private void SyncWiring()
    {
        var outputs = Outputs.Items.OfType<VolumeRow>().Where(r => r is MixerItem or ComboItem { IsMicrophone: false }).Select(WiringFor).ToList();
        var inputs = Inputs.Items.OfType<VolumeRow>().Where(r => r is InputItem or ComboItem { IsMicrophone: true }).Select(WiringFor).ToList();
        var keep = outputs.Concat(inputs).Select(w => w.Key).ToHashSet();
        foreach (var gone in _wiring.Keys.Where(k => !keep.Contains(k)).ToList()) _wiring.Remove(gone);
        ShowIn(WiringOutputs, outputs);
        ShowIn(WiringInputs, inputs);
        WiringOutputs.Summary = Outputs.Summary;
        WiringInputs.Summary = CountOf(inputs.Count, "device");
    }

    private static void ShowIn<T>(VolumeGroup group, IReadOnlyList<T> items)
    {
        if (items.Cast<object>().SequenceEqual(group.Items)) return;
        group.Items.Clear();
        foreach (var item in items) group.Items.Add(item!);
    }

    private WiringRow WiringFor(VolumeRow row)
    {
        if (!_wiring.TryGetValue(row.Key, out var w) || !ReferenceEquals(w.Source, row))
            _wiring[row.Key] = w = new WiringRow(row, Gate(), Gate(), OpenWiringMenu);
        SyncWiringRow(w);
        return w;
    }

    /// <summary>One row again (its engine status changed).</summary>
    private void SyncWiringOf(string key)
    {
        if (_wiring.TryGetValue(key, out var w)) SyncWiringRow(w);
    }

    /// <summary>
    /// A row's cell from the routing model: one choice where the mode takes one device, else chips and "+ add"; a combined
    /// device's members as chips; a microphone's own words with "Combine with...".
    /// </summary>
    private void SyncWiringRow(WiringRow w)
    {
        w.Cable = CableStateOf(w.Key, _mixer.CombinedOf(w.Key)?.Cable);
        if (w.Source is ComboItem { IsMicrophone: false } && _modes.Check(SoundFeature.CombineSpeakers) is { Reason: { } blocked })
            w.Cable = new CableState(SoundModes.NeedsFullTag, blocked, IsCaution: true); // Fast mode: the engine says the same
        SyncCableSteps(w);
        switch (w.Source)
        {
            case MixerItem source:
                SyncOutputCell(w, source);
                break;
            case ComboItem:
                SyncMembers(w);
                break;
            default:
                w.MenuGate.Sync(CreateGateFor(true)); // the menu says why a new combined microphone can't be made now (its entries are dimmed)
                w.ShowFixed(ThisMicrophone, CombineWithLabel, CombineWithTip);
                break;
        }
    }

    private void SyncOutputCell(WiringRow w, MixerItem source)
    {
        var gate = source.IsMaster ? MasterOutputsGate() : _modes.Check(SoundFeature.ProgramSeveralOutputs);
        w.MenuGate.Sync(gate);
        var targets = TargetsOf(source);
        if (!gate.Allowed)
        {
            w.ShowSingle(RouteTextOf(source));
            return;
        }
        var removable = !source.IsMaster || targets.Count > 1; // the master always plays somewhere
        w.ShowChips(targets.Select(t => new WiringChip(t, ChipLabelOf(t), removable, () => RemoveTarget(w, t))),
            source.IsMaster ? null : FollowsMasterNote);
    }

    /// <summary>
    /// Whether the master can play on several devices: Fast mode can't (Switch to Full control); in Full control only the cable bus can,
    /// so with the master in Equalizer APO its devices are one choice (its Method link changes that).
    /// </summary>
    private Audio.Gate MasterOutputsGate() =>
        _mixer.SoundMode == SoundModes.Full && !MasterOnBus
            ? new Audio.Gate(SoundModes.ApoOneSpeaker, null, "Equalizer APO plays the master on one speaker.")
            : _modes.Check(SoundFeature.MasterSeveralOutputs);

    /// <summary>
    /// A combined device's members: a chip each (a microphone's words open its level, a speaker's are plain; the x takes it away and the
    /// last one stays), then "+ add".
    /// </summary>
    private void SyncMembers(WiringRow w)
    {
        w.MenuGate.Sync(Audio.Gate.Open);
        if (_mixer.CombinedOf(w.Key) is not { } c) return;
        var removable = c.Members.Count > 1;
        if (c.Kind == CombinedKind.Microphones)
            w.ShowChips(c.Members.Select(m => MemberChip(w, c.Key, m, removable)), null, WiringAdd.AddLabel, AddMicTip);
        else
            w.ShowChips(c.Members.Select(m => new WiringChip(m, ChipLabelOf(m), removable, () =>
            {
                _mixer.ToggleMember(c.Key, m);
                AfterWiringChange(w);
            })), null, WiringAdd.AddLabel, AddSpeakerTip);
    }

    private WiringMember MemberChip(WiringRow w, string key, string member, bool removable)
    {
        var name = NameOf(member);
        var menu = new MemberLevelMenu(name,
            () => _mixer.CombinedOf(key) is { } c ? (c.LevelOf(member), c.IsMuted(member)) : (CombinedDevice.DefaultMemberLevel, false),
            (level, muted) => _mixer.SetMemberLevel(key, member, level, muted));
        var tip = LoopbackInputs.RecordsPlayback(name) ? $"{MemberTip} {SoundModes.RecordsPlaybackTag}: {SoundModes.RecordsPlaybackTip}" : MemberTip;
        return new WiringMember(member, ChipLabelOf(member), tip, removable, menu, () =>
        {
            _mixer.ToggleMember(key, member);
            AfterWiringChange(w);
        });
    }

    private string ChipLabelOf(string target) =>
        !CombinedDevice.IsKey(target) && _mixer.Devices.Concat(_mixer.RecordingDevices).All(d => d.Id != target) && _mixer.Combined.All(c => c.Cable != target)
            ? $"{NameOf(target)} (not plugged in)"
            : NameOf(target);

    // ---- the cell's menu ----

    /// <summary>The menu opened: what can still be added (or chosen), from what is plugged in now.</summary>
    private void OpenWiringMenu(WiringRow w)
    {
        _modes.Refresh();
        SyncWiringRow(w);
        switch (w.Source)
        {
            case MixerItem source:
                var targets = TargetsOf(source);
                var choices = new List<WiringChoice>();
                if (!source.IsMaster && targets.Count > 0) choices.Add(new WiringChoice(FollowMasterChoice, null, null, () => ChooseTarget(w, DefaultTarget)));
                // The master in Equalizer APO (Full control) plays on one real speaker: combined speakers are dimmed with that reason.
                var combinedGate = source.IsMaster && _mixer.SoundMode == SoundModes.Full && !MasterOnBus ? MasterOutputsGate() : _modes.Check(SoundFeature.CombinedOutputs);
                choices.AddRange(DeviceChoices(targets, cables: !source.IsMaster && SoundModes.CablesOfferedAsDevices(_mixer.SoundMode), combinedGate, t => ChooseTarget(w, t)));
                w.MenuTitle = w.MenuGate.IsAvailable ? AddTitle : ChooseTitle;
                w.Choices = choices;
                break;
            case ComboItem combo:
                var members = TargetsOf(combo);
                w.MenuTitle = combo.IsMicrophone ? AddMicTitle : AddTitle;
                Action<string> add = m =>
                {
                    _mixer.ToggleMember(combo.Key, m);
                    AfterWiringChange(w);
                };
                var candidates = combo.IsMicrophone
                    ? MicrophoneChoices(members, add)
                    : CombinedDevice.Candidates(_mixer.Devices).Where(d => !members.Contains(d.Id)).Select(d => new WiringChoice(d.Name, null, null, () => add(d.Id)));
                w.Choices = candidates.Append(new WiringChoice(RenameChoice, null, null, () => _ = EditComboAsync(combo.Key), tip: RenameTip)).ToList();
                break;
            case InputItem input:
                w.MenuTitle = CombineWithTitle;
                w.Choices = CombineWithChoices(input).ToList();
                break;
        }
    }

    /// <summary>
    /// Playback devices not chosen yet, combined speakers (dimmed with <paramref name="combinedGate"/>'s reason, tagged with the mode
    /// that allows them, where they can't be chosen; dimmed too while one has no cable to play through), and free cables (programs in
    /// Full control only: in Fast mode nothing records a cable).
    /// </summary>
    private IEnumerable<WiringChoice> DeviceChoices(IReadOnlyList<string> chosen, bool cables, Audio.Gate combinedGate, Action<string> choose)
    {
        foreach (var d in _mixer.Devices.Where(d => !VirtualCables.IsCable(d.Name) && !chosen.Contains(d.Id)))
            yield return new WiringChoice(d.Name, null, null, () => choose(d.Id));

        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Speakers && !chosen.Contains(c.Key) && !(c.Cable.Length > 0 && chosen.Contains(c.Cable))))
        {
            var gate = SoundModes.CombinedChoiceGate(combinedGate, _engine.StatusOf(c.Key).Kind, c.Name);
            yield return new WiringChoice(c.Name, gate.SwitchTo?.Name ?? ComboItem.CombinedTag, gate.Reason, () => choose(c.Key));
        }

        if (!cables) yield break;
        foreach (var cable in FreeCables().Where(c => !chosen.Contains(c.PlaybackId)))
            yield return new WiringChoice(cable.Name, CableTag, null, () => choose(cable.PlaybackId));
    }

    /// <summary>
    /// Recording devices that can join a mix and are not in <paramref name="chosen"/>; one that records the PC's sound carries the caution
    /// tag. <paramref name="disabledReason"/> dims every entry (a new combined microphone that can't be made now).
    /// </summary>
    private IEnumerable<WiringChoice> MicrophoneChoices(IReadOnlyList<string> chosen, Action<string> choose, string? disabledReason = null) =>
        CombinedDevice.Candidates(_mixer.RecordingDevices).Where(d => !chosen.Contains(d.Id)).Select(d => LoopbackInputs.RecordsPlayback(d.Name)
            ? new WiringChoice(d.Name, SoundModes.RecordsPlaybackTag, disabledReason, () => choose(d.Id), caution: true, tip: SoundModes.RecordsPlaybackTip)
            : new WiringChoice(d.Name, null, disabledReason, () => choose(d.Id)));

    /// <summary>A microphone's "Combine with": every other microphone (a new combined microphone, named in the window), then "Add to ..." each combined microphone it is not in.</summary>
    private IEnumerable<WiringChoice> CombineWithChoices(InputItem input)
    {
        foreach (var choice in MicrophoneChoices(new[] { input.Id }, other => _ = CombineAsync(CombinedKind.Microphones, new[] { input.Id, other }),
                     CreateGateFor(true).Reason))
            yield return choice;
        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones && !c.Members.Contains(input.Id)))
            yield return new WiringChoice($"Add to {c.Name}", ComboItem.CombinedTag, null, () => _mixer.ToggleMember(c.Key, input.Id));
    }

    /// <summary>A menu choice: added (several devices) or the one device (one choice), at once.</summary>
    private void ChooseTarget(WiringRow w, string target)
    {
        if (w.Source is not MixerItem source) return;
        if (source.IsMaster) ChooseMasterTarget(target, w.MenuGate.IsAvailable);
        else ChooseProgramTarget(source.Key, target, w.MenuGate.IsAvailable);
        AfterWiringChange(w);
    }

    /// <summary>A chip's x: that device is taken away (the master keeps its last one).</summary>
    private void RemoveTarget(WiringRow w, string target)
    {
        if (w.Source is not MixerItem source) return;
        if (source.IsMaster) ChooseMasterTarget(target, several: true);
        else ChooseProgramTarget(source.Key, target, several: true);
        AfterWiringChange(w);
    }

    private void AfterWiringChange(WiringRow w)
    {
        SyncWiringRow(w);
        SyncRow(w.Source);
    }

    // ---- the Cable column ----

    /// <summary>A microphone's or combined microphone's row: its run plays into a cable that apps record from.</summary>
    private bool IsRecordingRow(string key) => MixerService.IsInput(key) || _mixer.CombinedOf(key)?.Kind == CombinedKind.Microphones;

    /// <summary>
    /// The cable a row's run uses now (or the cable a combined device keeps in Full control, <paramref name="remembered"/>), the engine's
    /// own words when it waits for one (the tooltip says why when the planner knows: the master bus holds it), or nothing.
    /// </summary>
    private CableState CableStateOf(string key, string? remembered)
    {
        var status = _engine.StatusOf(key);
        if (SoundModes.IsCableMissing(status.Kind))
            return new CableState(status.Brief, status.Detail ?? (IsRecordingRow(key) ? "Starts when a cable is free" : "Plays without effects until a cable is free"), IsCaution: true);
        var id = _engine.CableOf(key) ?? (_mixer.SoundMode == SoundModes.Full && remembered is { Length: > 0 } ? remembered : null);
        if (id is null) return CableState.None;
        var cable = _modes.Current?.Cables.FirstOrDefault(c => c.PlaybackId == id);
        var name = cable?.Name ?? _mixer.DeviceNameOf(id);
        var tip = IsRecordingRow(key) ? SoundModes.AppsRecordFrom(cable?.RecordingName ?? name)
            : CombinedDevice.IsKey(key) ? $"Plays on every member of {name}"
            : $"Sound goes through {name} to the engine";
        return new CableState(name, tip, IsCaution: false);
    }

    /// <summary>
    /// While a microphone or combined device waits for a cable: the step that gets one (Install VB-CABLE, More cables) and, when the
    /// master bus holds the only pair apps can record from (the status's detail, from the planner), "Switch to Fast". A combined
    /// speaker that Fast mode cannot run says so with "Switch to Full control".
    /// </summary>
    private void SyncCableSteps(WiringRow w)
    {
        var waiting = w.Cable.IsCaution && (IsRecordingRow(w.Key) || CombinedDevice.IsKey(w.Key));
        var step = waiting ? CableStepFor(w.Cable.Text) : null;
        if (step?.Label != w.CableAction?.Label) w.CableAction = step;
        if (w.Source is ComboItem { IsMicrophone: false })
            w.CableSwitch.Sync(_modes.Check(SoundFeature.CombineSpeakers));
        else
            w.CableSwitch.Sync(waiting && _mixer.SoundMode == SoundModes.Full && _engine.StatusOf(w.Key).Detail is { } held
                ? new Audio.Gate(held, SoundModes.Fast)
                : Audio.Gate.Open);
    }

    // ---- the jump from the Mixer ----

    /// <summary>Opens the Wiring tab at a row (its group opens, the page moves the keyboard to it).</summary>
    private void ShowWiring(string key)
    {
        Tabs.Select(WiringTab);
        var target = _wiring.GetValueOrDefault(key);
        var group = target switch
        {
            WiringRow { Source: InputItem or ComboItem { IsMicrophone: true } } => WiringInputs,
            WiringRow => WiringOutputs,
            _ => null,
        };
        if (group is null) return;
        group.IsExpanded = true;
        RevealRequested?.Invoke(target!);
    }
}
