using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab: one table row per source (the master, programs and combined speakers in Outputs; recording devices and combined
/// microphones in Microphones), all made from the Mixer's own rows and the routing model (<see cref="TargetsOf"/>); a cell's menu
/// (what can still be added, dimmed where it can't be used, with the reason; a microphone's "Combine with"); the
/// Cable column with the step that gets a cable; and the jump from a Mixer row.
public sealed partial class VolumePageViewModel
{
    private const string ThisMicrophone = "This microphone", FollowsMasterNote = "Follows the master";
    private const string FollowMasterChoice = "Master (default device)";
    private const string CableTag = "Cable", BusCableTag = "Master bus", HeldCableTag = "In use";
    private const string AddTitle = "Add a device", ChooseTitle = "Choose a device";
    private const string CombineSpeakersText = "+ Combine speakers", CombineMicrophonesText = "+ Combine microphones";
    private const string AddSecondChoice = "+ Add a second device", AddSecondTip = "Opens Wiring, where this row can play on several devices";
    private const string CombineWithTitle = "Combine with", CombineWithLabel = "Combine with...", CombineWithTip = "Mixes this microphone with others into one";
    private const string AddMicTitle = "Add a microphone", AddMicTip = "Add a microphone to the mix";
    private const string AddSpeakerTip = "Add a device that plays the same sound";
    private const string MemberTip = "Its level and mute in the mix. Its own effects are not included.";
    private const string RenameChoice = "Rename...", RenameTip = "Its name and devices in the Combine devices window";

    /// The master, programs and combined speakers, with "+ Combine speakers" at the right of the header (set in the constructor).
    public VolumeGroup WiringOutputs { get; }

    /// Recording devices and combined microphones, with "+ Combine microphones" at the right of the header (set in the constructor).
    public VolumeGroup WiringInputs { get; }

    private readonly Dictionary<string, WiringRow> _wiring = new();

    /// A jump from the Mixer wants this Wiring row or card shown (the page moves the keyboard to it and scrolls it into view).
    public event Action<object>? RevealRequested;

    private VolumeGroup NewWiringOutputs() => new("Volume/Wiring/Outputs", "Outputs")
    {
        ExpandedByDefault = true,
        RouteColumn = PlaysOnWords,
        Tip = WiringTip,
        SummaryBesideName = true, // the header's right end holds "+ Combine speakers"
        ToolGate = CombineSpeakersGate,
        Tool = new CardAction(CombineSpeakersText, "Plays the same sound on several speakers", CombineSpeakersCommand, Main: true),
    };

    private VolumeGroup NewWiringInputs() => new("Volume/Wiring/Microphones", "Microphones")
    {
        ExpandedByDefault = true,
        RouteColumn = RecordsFromWords,
        SummaryBesideName = true, // the header's right end holds "+ Combine microphones"
        ToolGate = CombineMicrophonesGate,
        Tool = new CardAction(CombineMicrophonesText, "Mixes several microphones into one that apps record from", CombineMicrophonesCommand, Main: true),
    };

    /// Brings the Wiring tab in line with the Mixer's rows and the routing model.
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
        {
            // The menu is made first and finds its row when it opens (by then the row exists).
            WiringRow? made = null;
            made = new WiringRow(row, new PlaysOnMenu(new ControlGate(), _ => OpenWiringMenu(made!), marksCurrent: false));
            _wiring[row.Key] = w = made;
        }
        SyncWiringRow(w);
        return w;
    }

    /// One row again (its engine status changed).
    private void SyncWiringOf(string key)
    {
        if (_wiring.TryGetValue(key, out var w)) SyncWiringRow(w);
    }

    /// A row's cell from the routing model: one choice where the row takes one device, else chips and "+ add"; a combined
    /// device's members as chips; a microphone's own words with "Combine with...".
    private void SyncWiringRow(WiringRow w)
    {
        w.Cable = CableStateOf(w.Key, _mixer.CombinedOf(w.Key)?.Cable);
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
                w.Menu.Gate.Sync(CreateGateFor(true)); // the menu says why a new combined microphone can't be made now (its entries are dimmed)
                w.ShowFixed(ThisMicrophone, CombineWithLabel, CombineWithTip);
                break;
        }
    }

    private void SyncOutputCell(WiringRow w, MixerItem source)
    {
        var gate = OutputsGateOf(source);
        w.Menu.Gate.Sync(gate);
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

    /// Whether the master or a program can play on several devices now (the Plays on menus of both tabs).
    private Audio.Gate OutputsGateOf(MixerItem source) => source.IsMaster ? MasterOutputsGate() : Audio.Gate.Open;

    /// Whether the master can play on several devices: only the cable bus can, so with the master in Equalizer APO its devices are
    /// one choice (its Method link changes that).
    private Audio.Gate MasterOutputsGate() =>
        MasterOnBus ? Audio.Gate.Open : new Audio.Gate(EffectsRules.ApoOneSpeaker, "Equalizer APO plays the master on one speaker.");

    /// A combined device's members: a chip each (a microphone's words open its level, a speaker's are plain; the x takes it away and the
    /// last one stays), then "+ add".
    private void SyncMembers(WiringRow w)
    {
        w.Menu.Gate.Sync(Audio.Gate.Open);
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
        var tip = LoopbackInputs.RecordsPlayback(name) ? $"{MemberTip} {EffectsRules.RecordsPlaybackTag}: {EffectsRules.RecordsPlaybackTip}" : MemberTip;
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

    /// The menu opened: what can still be added (or chosen), from what is plugged in now.
    private void OpenWiringMenu(WiringRow w)
    {
        _support.Refresh();
        SyncWiringRow(w);
        switch (w.Source)
        {
            case MixerItem source:
                FillPlaysOn(w.Menu, source);
                break;
            case ComboItem combo:
                var members = TargetsOf(combo);
                w.Menu.Title = combo.IsMicrophone ? AddMicTitle : AddTitle;
                Action<string> add = m =>
                {
                    _mixer.ToggleMember(combo.Key, m);
                    AfterWiringChange(w);
                };
                var candidates = combo.IsMicrophone
                    ? MicrophoneChoices(members, add)
                    : CombinedDevice.Candidates(_mixer.Devices).Where(d => !members.Contains(d.Id)).Select(d => new WiringChoice(d.Name, null, null, () => add(d.Id)));
                w.Menu.Choices = candidates.Append(new WiringChoice(RenameChoice, null, null, () => _ = EditComboAsync(combo.Key), tip: RenameTip)).ToList();
                break;
            case InputItem input:
                w.Menu.Title = CombineWithTitle;
                w.Menu.Choices = CombineWithChoices(input).ToList();
                break;
        }
    }

    /// The one "Plays on" menu of the master or a program, for both tabs. The Wiring cell's menu lists what can still be added (several
    /// devices) or chosen (one), its chips show the rest; the Mixer chip's menu (<see cref="PlaysOnMenu.MarksCurrent"/>) lists the devices
    /// in use too, ticked, a pick makes the device the only one, and "+ Add a second device" opens Wiring where several are allowed. The
    /// same entries, greying and reasons in both (<see cref="DeviceChoices"/>), the same gate note.
    private void FillPlaysOn(PlaysOnMenu menu, MixerItem source)
    {
        var gate = OutputsGateOf(source);
        menu.Gate.Sync(gate);
        var adds = gate.Allowed && !menu.MarksCurrent;
        var targets = TargetsOf(source);
        var choices = new List<WiringChoice>();
        void Choose(string target)
        {
            if (source.IsMaster) ChooseMasterTarget(target, adds);
            else ChooseProgramTarget(source.Key, target, adds);
            AfterTargetChange(source);
        }
        if (!source.IsMaster && (menu.MarksCurrent || targets.Count > 0))
            choices.Add(new WiringChoice(FollowMasterChoice, null, null, () => Choose(DefaultTarget), isCurrent: menu.MarksCurrent && targets.Count == 0));
        if (menu.MarksCurrent) choices.AddRange(targets.Select(t => new WiringChoice(ChipLabelOf(t), null, null, () => Choose(t), isCurrent: true)));
        // The master in Equalizer APO plays on one real speaker: combined speakers are dimmed with that reason.
        var combinedGate = source.IsMaster ? MasterOutputsGate() : Audio.Gate.Open;
        choices.AddRange(DeviceChoices(targets, onlyBusDevice: source.IsMaster && MasterOnBus && !adds, combinedGate, Choose));
        if (menu.MarksCurrent && gate.Allowed) choices.Add(new WiringChoice(AddSecondChoice, null, null, () => ShowWiring(source.Key), tip: AddSecondTip));
        menu.Title = menu.MarksCurrent ? PlaysOnWords : gate.Allowed ? AddTitle : ChooseTitle;
        menu.Choices = choices;
    }

    /// The Mixer chip's menu is opening: what this PC offers is read again first (the Wiring cell's menu does the same).
    private void OpenPlaysOn(PlaysOnMenu menu, MixerItem source)
    {
        _support.Refresh();
        FillPlaysOn(menu, source);
    }

    /// Where a row plays changed: its Wiring row and its Mixer row follow.
    private void AfterTargetChange(MixerItem source)
    {
        SyncWiringOf(source.Key);
        SyncRow(source);
    }

    /// Every playback device not chosen yet, virtual cables included (tagged; one OmniControl runs through is dimmed with what uses it), and
    /// combined speakers (dimmed with <paramref name="combinedGate"/>'s reason where they can't be chosen; dimmed too while one has no cable to play through).
    /// <param name="onlyBusDevice">The pick becomes the master bus's one device, which must be real speakers (a cable only as a second device).</param>
    private IEnumerable<WiringChoice> DeviceChoices(IReadOnlyList<string> chosen, bool onlyBusDevice, Audio.Gate combinedGate, Action<string> choose)
    {
        foreach (var d in _mixer.Devices.Where(d => !chosen.Contains(d.Id)))
            yield return VirtualCables.IsCable(d.Name) ? CableChoice(d, onlyBusDevice, choose) : new WiringChoice(d.Name, null, null, () => choose(d.Id));

        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Speakers && !chosen.Contains(c.Key) && !(c.Cable.Length > 0 && chosen.Contains(c.Cable))))
        {
            var gate = EffectsRules.CombinedChoiceGate(combinedGate, _engine.StatusOf(c.Key).Kind, c.Name);
            yield return new WiringChoice(c.Name, ComboItem.CombinedTag, gate.Reason, () => choose(c.Key));
        }
    }

    /// A virtual cable as a Plays on choice: the user's own to play on, unless one of OmniControl's runs goes through it (playing into it
    /// would join that run's sound) or it would be the master bus's only device.
    private WiringChoice CableChoice(AudioDevice cable, bool onlyBusDevice, Action<string> choose)
    {
        var user = _engine.RowOnCable(new[] { cable.Id });
        var held = user is not null || _engine.CablesInUse.Contains(cable.Id);
        var reason = user == MixerService.MasterKey ? "OmniControl's master bus runs through this cable"
            : user is not null ? $"OmniControl runs {RowOf(user)?.Name ?? user} through this cable"
            : held ? "OmniControl uses this cable"
            : onlyBusDevice ? "The master bus plays on real speakers; a cable can be a second device"
            : null;
        return new WiringChoice(cable.Name, user == MixerService.MasterKey ? BusCableTag : held ? HeldCableTag : CableTag, reason, () => choose(cable.Id),
            tip: "A virtual cable: what plays here comes out of its recording side");
    }

    /// Recording devices that can join a mix and are not in <paramref name="chosen"/>; one that records the PC's sound carries the caution
    /// tag. <paramref name="disabledReason"/> dims every entry (a new combined microphone that can't be made now).
    private IEnumerable<WiringChoice> MicrophoneChoices(IReadOnlyList<string> chosen, Action<string> choose, string? disabledReason = null) =>
        CombinedDevice.Candidates(_mixer.RecordingDevices).Where(d => !chosen.Contains(d.Id)).Select(d => LoopbackInputs.RecordsPlayback(d.Name)
            ? new WiringChoice(d.Name, EffectsRules.RecordsPlaybackTag, disabledReason, () => choose(d.Id), caution: true, tip: EffectsRules.RecordsPlaybackTip)
            : new WiringChoice(d.Name, null, disabledReason, () => choose(d.Id)));

    /// A microphone's "Combine with": every other microphone (a new combined microphone, named in the window), then "Add to ..." each combined microphone it is not in.
    private IEnumerable<WiringChoice> CombineWithChoices(InputItem input)
    {
        foreach (var choice in MicrophoneChoices(new[] { input.Id }, other => _ = CombineAsync(CombinedKind.Microphones, new[] { input.Id, other }),
                     CreateGateFor(true).Reason))
            yield return choice;
        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones && !c.Members.Contains(input.Id)))
            yield return new WiringChoice($"Add to {c.Name}", ComboItem.CombinedTag, null, () => _mixer.ToggleMember(c.Key, input.Id));
    }

    /// A chip's x: that device is taken away (the master keeps its last one).
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

    /// A microphone's or combined microphone's row: its run plays into a cable that apps record from.
    private bool IsRecordingRow(string key) => MixerService.IsInput(key) || _mixer.CombinedOf(key)?.Kind == CombinedKind.Microphones;

    /// The cable a row's run uses now (or the cable a combined device keeps, <paramref name="remembered"/>), the engine's
    /// own words when it waits for one (the tooltip says why when the planner knows: the master bus holds it), or nothing.
    private CableState CableStateOf(string key, string? remembered)
    {
        var status = _engine.StatusOf(key);
        if (EffectsRules.IsCableMissing(status.Kind))
            return new CableState(status.Brief, status.Detail ?? (IsRecordingRow(key) ? "Starts when a cable is free" : "Plays without effects until a cable is free"), IsCaution: true);
        var id = _engine.CableOf(key) ?? (remembered is { Length: > 0 } ? remembered : null);
        if (id is null) return CableState.None;
        var cable = _support.Current?.Cables.FirstOrDefault(c => c.PlaybackId == id);
        var name = cable?.Name ?? _mixer.DeviceNameOf(id);
        var tip = IsRecordingRow(key) ? EffectsRules.AppsRecordFrom(cable?.RecordingName ?? name)
            : CombinedDevice.IsKey(key) ? $"Plays on every member of {name}"
            : $"Sound goes through {name} to the engine";
        return new CableState(name, tip, IsCaution: false);
    }

    /// While a microphone or combined device waits for a cable: the step that gets one (Install VB-CABLE, More cables).
    private void SyncCableSteps(WiringRow w)
    {
        var waiting = w.Cable.IsCaution && (IsRecordingRow(w.Key) || CombinedDevice.IsKey(w.Key));
        var step = waiting ? CableStepFor(w.Cable.Text) : null;
        if (step?.Label != w.CableAction?.Label) w.CableAction = step;
    }

    // ---- the jump from the Mixer ----

    /// Opens the Wiring tab at a row (its group opens, the page moves the keyboard to it).
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

    private object? _pendingReveal;

    /// A jump from another window (a device folder's "Change in Mixer"): the Advanced Mixer with that device's own row shown when the
    /// Outputs list has one, else the master row, whose Method every playback device runs through. The page moves the keyboard there once it
    /// is on screen (<see cref="TakePendingReveal"/>).
    public void ShowMixerFor(string deviceId)
    {
        IsEasy = false;
        Tabs.Select(MixerTab);
        Outputs.IsExpanded = true;
        object row = _mixer.Combined.FirstOrDefault(c => c.Kind == CombinedKind.Speakers && c.Cable == deviceId) is { } device
                     && _combos.TryGetValue(device.Key, out var combo) ? combo : _master;
        if (RevealRequested is { } reveal) reveal(row);
        else _pendingReveal = row;
    }

    /// The row a jump asked for while the page was not on screen; asked for once, by the page when it comes on screen.
    public object? TakePendingReveal()
    {
        var row = _pendingReveal;
        _pendingReveal = null;
        return row;
    }
}
