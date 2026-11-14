using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Combining devices: the one path for the Outputs table's "+ Combine speakers", the Microphones table's "+ Combine microphones" and a
/// microphone's "Combine with", the search card, and "Rename..." in a combined row's menu, all through the Combine devices window;
/// the steps that get a virtual cable.
/// </summary>
public sealed partial class VolumePageViewModel
{
    /// <summary>A new combined device's name before the user types one ("Combined microphone 2" when that is taken).</summary>
    private const string NewMicrophoneName = "Combined microphone", NewSpeakersName = "Combined speakers";

    [RelayCommand]
    private Task CombineSpeakers() => CombineAsync(CombinedKind.Speakers, Array.Empty<string>());

    [RelayCommand]
    private Task CombineMicrophones() => CombineAsync(CombinedKind.Microphones, Array.Empty<string>());

    /// <param name="kind">The kind to combine (the window hides the choice); null = the user chooses (speakers first where this mode allows them).</param>
    /// <param name="ticked">Devices already ticked (a microphone's "Combine with").</param>
    private async Task CombineAsync(CombinedKind? kind, IReadOnlyList<string> ticked)
    {
        var start = kind ?? (CombineSpeakersGate.IsAvailable ? CombinedKind.Speakers : CombinedKind.Microphones);
        CombineDeviceViewModel? dialog = null;
        dialog = NewCombineDialog(null, new CombineStart(start, kind is not null, ticked, FreeName(start)),
            microphones => CableLineFor(microphones, null, () => dialog?.RefreshCable()));
        if (!await _combine(dialog)) return;
        var device = dialog.Result();
        _mixer.AddCombined(device);
        _report($"{device.Name} created");
    }

    /// <summary>Rename: a combined device's name and members in the Combine devices window.</summary>
    private async Task EditComboAsync(string key)
    {
        if (_mixer.CombinedOf(key) is not { } c) return;
        CombineDeviceViewModel? dialog = null;
        dialog = NewCombineDialog(c, null, microphones => CableLineFor(microphones, c, () => dialog?.RefreshCable()));
        if (!await _combine(dialog)) return;
        var edited = dialog.Result();
        _mixer.EditCombined(key, edited.Name, edited.Members);
        _report($"{edited.Name} saved");
    }

    private CombineDeviceViewModel NewCombineDialog(CombinedDevice? editing, CombineStart? start, Func<bool, CombineCableLine> cableLine) => new(
        CombinedDevice.Candidates(_mixer.Devices).ToList(),
        CombinedDevice.Candidates(_mixer.RecordingDevices).ToList(),
        cableLine, CreateGateFor, RecordingNameFor(editing), editing, start);

    /// <summary>
    /// Whether a new combined device of this kind can be created and run now (<see cref="SoundModes.CombineGate"/>): the "+ Combine"
    /// pills, the window's kind choice and its Create button, and a microphone's "Combine with" all read this one answer.
    /// </summary>
    private Audio.Gate CreateGateFor(bool microphones) => SoundModes.CombineGate(microphones, _mixer.SoundMode, _modes.Current,
        FreeCableFor(microphones) is not null, microphones ? BusHeldCable() : BusCableName());

    /// <summary>Full control: the short name of the cable the master bus holds (any pair), else null.</summary>
    private string? BusCableName() =>
        _mixer.SoundMode == SoundModes.Full && _engine.CableOf(null) is { } bus
            ? _modes.Current?.Cables.FirstOrDefault(c => c.PlaybackId == bus)?.Name
            : null;

    /// <summary>"Combined microphone" or "Combined speakers" (by kind), or the first numbered one not taken.</summary>
    private string FreeName(CombinedKind kind)
    {
        var taken = _mixer.Combined.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var first = kind == CombinedKind.Microphones ? NewMicrophoneName : NewSpeakersName;
        var name = first;
        for (var n = 2; taken.Contains(name); n++) name = $"{first} {n}";
        return name;
    }

    /// <summary>
    /// The window's cable line: the cable the device uses or gets, or what it waits for with the step that gets one; for microphones in
    /// Full control while the master bus holds the only pair apps can record from, that fact and "Switch to Fast".
    /// </summary>
    private CombineCableLine CableLineFor(bool microphones, CombinedDevice? editing, Action switched)
    {
        if (editing is null && CreateGateFor(microphones) is { Allowed: false } blocked) return BlockedLine(blocked, switched);
        if (editing is not null && CableStateOf(editing.Key, editing.Cable) is { HasTag: true } state)
            return state.IsCaution ? WaitingLine(state.Text, microphones, switched) : new CombineCableLine($"Uses {state.Text}.", null, null);
        if (FreeCableFor(microphones) is { } free) return new CombineCableLine($"{free.Name} is ready for it.", null, null);
        return WaitingLine(CableMissing(), microphones, switched);
    }

    /// <summary>
    /// A new combined device that can't be created now: why, the step that gets what is missing (install VB-CABLE, more cables) and
    /// the Switch to the mode that allows it; the window's Create stays dimmed (<see cref="CombineCableLine.Blocks"/>).
    /// </summary>
    private CombineCableLine BlockedLine(Audio.Gate blocked, Action switched)
    {
        var link = new ModeGate(mode =>
        {
            SetSoundMode(mode);
            switched();
        }, ModeWhy);
        link.Sync(blocked);
        var step = _modes.Current is { } support && SoundModes.CablesMissing(support) is { } missing ? CableStepFor(missing)
            : blocked.SwitchTo is null ? MoreCablesAction() : null;
        return new CombineCableLine(link.Reason ?? blocked.Reason!, step, link, Blocks: true);
    }

    private CombineCableLine WaitingLine(string missing, bool microphones, Action switched)
    {
        if (microphones && BusHeldCable() is { } held)
        {
            var gate = new ModeGate(mode =>
            {
                SetSoundMode(mode);
                switched();
            }, ModeWhy);
            gate.Sync(new Audio.Gate(SoundModes.BusHoldsCable(held), SoundModes.Fast));
            return new CombineCableLine(SoundModes.BusHoldsCable(held), CableStepFor(missing), gate);
        }
        return new CombineCableLine($"{missing}: it waits until a cable is free.", CableStepFor(missing), null);
    }

    /// <summary>A cable nothing uses now; for microphones one apps can record from, with no other input of its pair in use.</summary>
    private VirtualCable? FreeCableFor(bool microphones)
    {
        var all = _modes.Current?.Cables ?? Array.Empty<VirtualCable>();
        var free = FreeCables().ToList();
        if (!microphones) return free.FirstOrDefault();
        var pair = VirtualCables.Pairs(all).FirstOrDefault(p => p.RecordingId is not null && p.PlaybackIds.All(id => free.Any(f => f.PlaybackId == id)));
        return pair is null ? null : free.First(f => pair.PlaybackIds.Contains(f.PlaybackId));
    }

    /// <summary>Full control: the short name of the master bus's cable when apps could record from its pair (it is not free for a microphone); else null.</summary>
    private string? BusHeldCable()
    {
        if (_mixer.SoundMode != SoundModes.Full || _engine.CableOf(null) is not { } bus) return null;
        var all = _modes.Current?.Cables ?? Array.Empty<VirtualCable>();
        return VirtualCables.Pairs(all).Any(p => p.RecordingId is not null && p.PlaybackIds.Contains(bus))
            ? all.FirstOrDefault(c => c.PlaybackId == bus)?.Name
            : null;
    }

    /// <summary>What apps pick as the combined microphone: its cable's recording side ("CABLE Output"), or a free one's; null when none is known.</summary>
    private string? RecordingNameFor(CombinedDevice? editing)
    {
        if (editing is null) return FreeCableFor(microphones: true)?.RecordingName;
        var id = _engine.CableOf(editing.Key) ?? (editing.Cable.Length > 0 ? editing.Cable : null);
        return (_modes.Current?.Cables ?? Array.Empty<VirtualCable>()).FirstOrDefault(c => c.PlaybackId == id)?.RecordingName;
    }

    /// <summary>
    /// The cables the plan gives to no run. A combined device's remembered cable is not a hold: the planner re-gives it only while its
    /// pair is free (and then lists it in <see cref="EffectsEngine.CablesInUse"/>), so a stale one never makes a free cable look taken.
    /// </summary>
    private IEnumerable<VirtualCable> FreeCables() =>
        (_modes.Current?.Cables ?? Array.Empty<VirtualCable>()).Where(c => !_engine.CablesInUse.Contains(c.PlaybackId));

    /// <summary>What a new cable user would wait for: VB-CABLE itself, a cable switched back on, or one more cable.</summary>
    private string CableMissing() => _modes.Current is { } support ? SoundModes.CablesMissing(support) ?? SoundModes.NeedsAnotherCable : SoundModes.NeedsCable;

    /// <summary>The step that gets what <paramref name="missing"/> says is missing (install VB-CABLE, or more cables); null = nothing missing.</summary>
    private ModeCardAction? CableStepFor(string? missing) => missing switch
    {
        SoundModes.NeedsCable => CableSetupAction(),
        SoundModes.NeedsAnotherCable => MoreCablesAction(),
        _ => null,
    };

    /// <summary>Install VB-CABLE: its own installer when it was placed next to OmniControl, else its download page.</summary>
    private ModeCardAction CableSetupAction() =>
        new("Install VB-CABLE", "Runs VB-CABLE's installer, or opens its download page",
            new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Full, null, _installer.InstallCableAsync())), Main: true);

    /// <summary>More cables: VB-Audio's page (each combined device, processed microphone or program needs its own cable).</summary>
    private ModeCardAction MoreCablesAction() =>
        new("More cables", "Opens VB-Audio's page: each combined device or processed row needs its own cable",
            new RelayCommand(() => _report(EngineInstaller.OpenCablePage().Text)));
}
