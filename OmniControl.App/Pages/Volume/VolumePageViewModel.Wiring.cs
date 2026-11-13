using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Wiring tab: one table row per source (the master and programs in Outputs, recording devices in Microphones) and one card per
/// combined device, all made from the Mixer's own rows and the routing model (<see cref="TargetsOf"/>); a cell's menu (what can still
/// be added, dimmed where the mode forbids it with the reason and one Switch); the Cable column; and the jump from a Mixer row.
/// </summary>
public sealed partial class VolumePageViewModel
{
    private const string ThisMicrophone = "This microphone", FollowsMasterNote = "Follows the master";
    private const string FollowMasterChoice = "Master (follows the default device)";
    private const string CombinedTag = "Combined", CableTag = "Cable";
    private const string AddTitle = "Add a device", ChooseTitle = "Choose a device";

    public VolumeGroup WiringOutputs { get; } = new("Volume/Wiring/Outputs", "Outputs") { ExpandedByDefault = true, RouteColumn = "Plays on" };
    public VolumeGroup WiringInputs { get; } = new("Volume/Wiring/Microphones", "Microphones") { ExpandedByDefault = true, RouteColumn = "Records from" };
    public VolumeGroup CombinedDevices { get; } = new("Volume/Wiring/Combined", "Combined devices") { ExpandedByDefault = true };

    private readonly Dictionary<string, WiringRow> _wiring = new();
    private readonly Dictionary<string, CombinedCard> _cards = new();

    /// <summary>A jump from the Mixer wants this Wiring row or card shown (the page moves the keyboard to it and scrolls it into view).</summary>
    public event Action<object>? RevealRequested;

    /// <summary>Brings the Wiring tab in line with the Mixer's rows and the routing model.</summary>
    private void SyncWiring()
    {
        var outputs = Outputs.Items.OfType<MixerItem>().Select(WiringFor).ToList();
        var inputs = Inputs.Items.OfType<InputItem>().Select(WiringFor).ToList();
        var cards = _mixer.Combined.Select(c => _combos.GetValueOrDefault(c.Key)).OfType<ComboItem>().Select(CardFor).ToList();
        var keep = outputs.Concat(inputs).Select(w => w.Key).ToHashSet();
        foreach (var gone in _wiring.Keys.Where(k => !keep.Contains(k)).ToList()) _wiring.Remove(gone);
        var keepCards = cards.Select(c => c.Key).ToHashSet();
        foreach (var gone in _cards.Keys.Where(k => !keepCards.Contains(k)).ToList()) _cards.Remove(gone);
        ShowIn(WiringOutputs, outputs);
        ShowIn(WiringInputs, inputs);
        ShowIn(CombinedDevices, cards);
        WiringOutputs.Summary = Outputs.Summary;
        WiringInputs.Summary = CountOf(inputs.Count, "device");
        CombinedDevices.Summary = CountOf(cards.Count, "device");
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
            _wiring[row.Key] = w = new WiringRow(row, Gate(), OpenWiringMenu);
        SyncWiringRow(w);
        return w;
    }

    private CombinedCard CardFor(ComboItem row)
    {
        if (!_cards.TryGetValue(row.Key, out var card) || !ReferenceEquals(card.Source, row))
            _cards[row.Key] = card = new CombinedCard(row, () => _ = EditComboAsync(row.Key));
        SyncCard(card);
        return card;
    }

    /// <summary>One row or card again (its engine status changed).</summary>
    private void SyncWiringOf(string key)
    {
        if (_wiring.TryGetValue(key, out var w)) SyncWiringRow(w);
        if (_cards.TryGetValue(key, out var card)) SyncCard(card);
    }

    /// <summary>A row's cell from the routing model: one choice where the mode takes one device, else chips and "+ add".</summary>
    private void SyncWiringRow(WiringRow w)
    {
        w.Cable = CableStateOf(w.Key, null);
        if (w.Source is not MixerItem source)
        {
            w.MenuGate.Sync(Audio.Gate.Open);
            w.ShowFixed(ThisMicrophone);
            return;
        }
        var gate = _modes.Check(source.IsMaster ? SoundFeature.MasterSeveralOutputs : SoundFeature.ProgramSeveralOutputs);
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

    private void SyncCard(CombinedCard card)
    {
        if (_mixer.CombinedOf(card.Key) is not { } c) return;
        card.MembersText = NamesOf(c.Members);
        card.Cable = CableStateOf(c.Key, c.Cable);
        var step = CableStepFor(card.Cable.IsCaution ? card.Cable.Text : null);
        if (step?.Label != card.CableAction?.Label) card.CableAction = step;
    }

    private string ChipLabelOf(string target) =>
        !CombinedDevice.IsKey(target) && _mixer.Devices.All(d => d.Id != target) && _mixer.Combined.All(c => c.Cable != target)
            ? $"{NameOf(target)} (not plugged in)"
            : NameOf(target);

    // ---- the cell's menu ----

    /// <summary>The menu opened: what can still be added (or chosen), from what is plugged in now.</summary>
    private void OpenWiringMenu(WiringRow w)
    {
        if (w.Source is not MixerItem source) return;
        _modes.Refresh();
        SyncWiringRow(w);
        var targets = TargetsOf(source);
        var choices = new List<WiringChoice>();
        if (!source.IsMaster && targets.Count > 0) choices.Add(new WiringChoice(FollowMasterChoice, null, null, () => ChooseTarget(w, DefaultTarget)));
        choices.AddRange(DeviceChoices(targets, cables: !source.IsMaster, t => ChooseTarget(w, t)));
        w.MenuTitle = w.MenuGate.IsAvailable ? AddTitle : ChooseTitle;
        w.Choices = choices;
    }

    /// <summary>
    /// Playback devices not chosen yet, combined speakers (dimmed, tagged with the mode that allows them, where this mode can't), and
    /// free cables (programs only).
    /// </summary>
    private IEnumerable<WiringChoice> DeviceChoices(IReadOnlyList<string> chosen, bool cables, Action<string> choose)
    {
        foreach (var d in _mixer.Devices.Where(d => !VirtualCables.IsCable(d.Name) && !chosen.Contains(d.Id)))
            yield return new WiringChoice(d.Name, null, null, () => choose(d.Id));

        var combinedGate = _modes.Check(SoundFeature.CombinedOutputs);
        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Speakers && !chosen.Contains(c.Key) && !(c.Cable.Length > 0 && chosen.Contains(c.Cable))))
            yield return new WiringChoice(c.Name, combinedGate.SwitchTo?.Name ?? CombinedTag, combinedGate.Reason, () => choose(c.Key));

        if (!cables) yield break;
        foreach (var cable in FreeCables().Where(c => !chosen.Contains(c.PlaybackId)))
            yield return new WiringChoice(cable.Name, CableTag, null, () => choose(cable.PlaybackId));
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

    /// <summary>
    /// The cable a row's run uses now (or the cable a combined device keeps in Full control, <paramref name="remembered"/>), the engine's
    /// own words when it waits for one, or nothing.
    /// </summary>
    private CableState CableStateOf(string key, string? remembered)
    {
        var status = _engine.StatusOf(key);
        if (SoundModes.IsCableMissing(status.Text))
            return new CableState(status.Text, "It plays without OmniControl's engine until a virtual cable is free", IsCaution: true);
        var id = _engine.CableOf(key) ?? (_mixer.SoundMode == SoundModes.Full && remembered is { Length: > 0 } ? remembered : null);
        if (id is null) return CableState.None;
        var cable = _modes.Current?.Cables.FirstOrDefault(c => c.PlaybackId == id);
        var name = cable?.Name ?? _mixer.DeviceNameOf(id);
        var tip = MixerService.IsInput(key) || _mixer.CombinedOf(key)?.Kind == CombinedKind.Microphones
                ? $"Apps record from {cable?.RecordingName ?? name}"
            : CombinedDevice.IsKey(key) ? $"Whatever plays on {name} plays on every member"
            : $"The sound goes through {name} to OmniControl's engine";
        return new CableState(name, tip, IsCaution: false);
    }

    // ---- the jump from the Mixer ----

    /// <summary>Opens the Wiring tab; with a key, at that row or card (its group opens, the page moves the keyboard to it).</summary>
    private void ShowWiring(string? key)
    {
        Tabs.Select(WiringTab);
        object? target = key is null ? null : _wiring.GetValueOrDefault(key) ?? (object?)_cards.GetValueOrDefault(key);
        var group = target switch
        {
            WiringRow { Source: InputItem } => WiringInputs,
            WiringRow => WiringOutputs,
            CombinedCard => CombinedDevices,
            _ => null,
        };
        if (group is null) return;
        group.IsExpanded = true;
        RevealRequested?.Invoke(target!);
    }
}
