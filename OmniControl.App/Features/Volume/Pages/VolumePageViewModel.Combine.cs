using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages;

/// Combining devices: the one path for the Outputs table's "+ Combine speakers", the Microphones table's "+ Combine microphones" and a
/// microphone's "Combine with", the search card, and "Rename..." in a combined row's menu, all through the Combine devices window;
/// the steps that get a virtual cable.
public sealed partial class VolumePageViewModel
{
    /// A new combined device's name before the user types one ("Combined microphone 2" when that is taken).
    private const string NewMicrophoneName = "Combined microphone", NewSpeakersName = "Combined speakers";

    [RelayCommand]
    private Task CombineSpeakers() => CombineAsync(CombinedKind.Speakers, Array.Empty<string>());

    [RelayCommand]
    private Task CombineMicrophones() => CombineAsync(CombinedKind.Microphones, Array.Empty<string>());

    /// <param name="kind">The kind to combine (the window hides the choice); null = the user chooses (speakers first).</param>
    /// <param name="ticked">Devices already ticked (a microphone's "Combine with").</param>
    private async Task CombineAsync(CombinedKind? kind, IReadOnlyList<string> ticked)
    {
        var start = kind ?? (CombineSpeakersGate.IsAvailable ? CombinedKind.Speakers : CombinedKind.Microphones);
        var dialog = NewCombineDialog(null, new CombineStart(start, kind is not null, ticked, FreeName(start)), microphones => CableLineFor(microphones, null));
        if (!await _combine(dialog)) return;
        var device = dialog.Result();
        _mixer.AddCombined(device);
        _report($"{device.Name} created");
    }

    /// Rename: a combined device's name and members in the Combine devices window.
    private async Task EditComboAsync(string key)
    {
        if (_mixer.CombinedOf(key) is not { } c) return;
        var dialog = NewCombineDialog(c, null, microphones => CableLineFor(microphones, c));
        if (!await _combine(dialog)) return;
        var edited = dialog.Result();
        _mixer.EditCombined(key, edited.Name, edited.Members);
        _report($"{edited.Name} saved");
    }

    private CombineDeviceViewModel NewCombineDialog(CombinedDevice? editing, CombineStart? start, Func<bool, CombineCableLine> cableLine) => new(
        CombinedDevice.Candidates(_mixer.Devices).ToList(),
        CombinedDevice.Candidates(_mixer.RecordingDevices).ToList(),
        cableLine, CreateGateFor, RecordingNameFor(editing), editing, start);

    /// Whether a new combined device of this kind can be created and run now (<see cref="EffectsRules.CombineGate"/>): the "+ Combine"
    /// pills, the window's kind choice and its Create button, and a microphone's "Combine with" all read this one answer.
    private Audio.Gate CreateGateFor(bool microphones) => EffectsRules.CombineGate(microphones, _support.Current,
        FreeCableFor(microphones) is not null, microphones ? BusHeldCable() : BusCableName());

    /// The short name of the cable the master bus holds (any pair), else null.
    private string? BusCableName() =>
        _engine.CableOf(null) is { } bus
            ? _support.Current?.Cables.FirstOrDefault(c => c.PlaybackId == bus)?.Name
            : null;

    /// "Combined microphone" or "Combined speakers" (by kind), or the first numbered one not taken.
    private string FreeName(CombinedKind kind)
    {
        var taken = _mixer.Combined.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var first = kind == CombinedKind.Microphones ? NewMicrophoneName : NewSpeakersName;
        var name = first;
        for (var n = 2; taken.Contains(name); n++) name = $"{first} {n}";
        return name;
    }

    /// The window's cable line: the cable the device uses or gets, or what it waits for with the step that gets one; for microphones
    /// while the master bus holds the only pair apps can record from, that fact.
    private CombineCableLine CableLineFor(bool microphones, CombinedDevice? editing)
    {
        if (editing is null && CreateGateFor(microphones) is { Allowed: false } blocked) return BlockedLine(blocked);
        if (editing is not null && CableStateOf(editing.Key, editing.Cable) is { HasTag: true } state)
            return state.IsCaution ? WaitingLine(state.Text, microphones) : new CombineCableLine($"Uses {state.Text}.", null);
        if (FreeCableFor(microphones) is { } free) return new CombineCableLine($"{free.Name} is ready for it.", null);
        return WaitingLine(CableMissing(), microphones);
    }

    /// A new combined device that can't be created now: why, the step that gets what is missing (install VB-CABLE, more cables) and
    /// the window's Create stays dimmed (<see cref="CombineCableLine.Blocks"/>).
    private CombineCableLine BlockedLine(Audio.Gate blocked)
    {
        var step = _support.Current is { } support && EffectsRules.CablesMissing(support) is { } missing ? CableStepFor(missing) : MoreCablesAction();
        return new CombineCableLine(blocked.Reason!, step, Blocks: true);
    }

    private CombineCableLine WaitingLine(string missing, bool microphones) =>
        microphones && BusHeldCable() is { } held
            ? new CombineCableLine(EffectsRules.BusHoldsCable(held), CableStepFor(missing))
            : new CombineCableLine($"{missing}: it waits until a cable is free.", CableStepFor(missing));

    /// A cable nothing uses now; for microphones one apps can record from, with no other input of its pair in use.
    private VirtualCable? FreeCableFor(bool microphones)
    {
        var all = _support.Current?.Cables ?? Array.Empty<VirtualCable>();
        var free = FreeCables().ToList();
        if (!microphones) return free.FirstOrDefault();
        var pair = VirtualCables.Pairs(all).FirstOrDefault(p => p.RecordingId is not null && p.PlaybackIds.All(id => free.Any(f => f.PlaybackId == id)));
        return pair is null ? null : free.First(f => pair.PlaybackIds.Contains(f.PlaybackId));
    }

    /// The short name of the master bus's cable when apps could record from its pair (it is not free for a microphone); else null.
    private string? BusHeldCable()
    {
        if (_engine.CableOf(null) is not { } bus) return null;
        var all = _support.Current?.Cables ?? Array.Empty<VirtualCable>();
        return VirtualCables.Pairs(all).Any(p => p.RecordingId is not null && p.PlaybackIds.Contains(bus))
            ? all.FirstOrDefault(c => c.PlaybackId == bus)?.Name
            : null;
    }

    /// What apps pick as the combined microphone: its cable's recording side ("CABLE Output"), or a free one's; null when none is known.
    private string? RecordingNameFor(CombinedDevice? editing)
    {
        if (editing is null) return FreeCableFor(microphones: true)?.RecordingName;
        var id = _engine.CableOf(editing.Key) ?? (editing.Cable.Length > 0 ? editing.Cable : null);
        return (_support.Current?.Cables ?? Array.Empty<VirtualCable>()).FirstOrDefault(c => c.PlaybackId == id)?.RecordingName;
    }

    /// The cables the plan gives to no run. A combined device's remembered cable is not a hold: the planner re-gives it only while its
    /// pair is free (and then lists it in <see cref="EffectsEngine.CablesInUse"/>), so a stale one never makes a free cable look taken.
    private IEnumerable<VirtualCable> FreeCables() =>
        (_support.Current?.Cables ?? Array.Empty<VirtualCable>()).Where(c => !_engine.CablesInUse.Contains(c.PlaybackId));

    /// What a new cable user would wait for: VB-CABLE itself, a cable switched back on, or one more cable.
    private string CableMissing() => _support.Current is { } support ? EffectsRules.CablesMissing(support) ?? EffectsRules.NeedsAnotherCable : EffectsRules.NeedsCable;

    /// The step that gets what <paramref name="missing"/> says is missing (install VB-CABLE, or more cables); null = nothing missing.
    private CardAction? CableStepFor(string? missing) => missing switch
    {
        EffectsRules.NeedsCable => CableSetupAction(),
        EffectsRules.NeedsAnotherCable => MoreCablesAction(),
        _ => null,
    };

    /// Install VB-CABLE: its own installer when it was placed next to OmniControl, else its download page.
    private CardAction CableSetupAction() =>
        new("Install VB-CABLE", "Runs VB-CABLE's installer, or opens its download page",
            new AsyncRelayCommand(() => RunSetupAsync(null, _installer.InstallCableAsync())), Main: true);

    /// More cables: VB-Audio's page (each combined device, processed microphone or program needs its own cable).
    private CardAction MoreCablesAction() =>
        new("More cables", "Opens VB-Audio's page: each combined device or processed row needs its own cable",
            new RelayCommand(() => _report(EngineInstaller.OpenCablePage().Text)));
}
