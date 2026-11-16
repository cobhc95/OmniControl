using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>
/// Makes <see cref="EasyFacts"/> from the live services: it asks each service what it already knows (Equalizer APO's checks, the
/// planner's cable rules, the mixer's chains) and keeps no state of its own. Reads only. The page hands in its own
/// two cable rules (a free cable for a kind, the master bus's cable) so Easy and Advanced give the same answer.
/// </summary>
public sealed class EasyEnvironment
{
    private readonly MixerService _mixer;
    private readonly EffectsSupportService _support;
    private readonly EffectsEngine _engine;
    private readonly Func<bool, VirtualCable?> _freeCable;
    private readonly Func<bool, string?> _busCable;

    /// <param name="freeCable">A cable that is free for a combined device (true = a microphone, one apps can record from), or null.</param>
    /// <param name="busCable">The short name of the cable the master bus holds that such a device would need (true = a microphone), or null.</param>
    public EasyEnvironment(MixerService mixer, EffectsSupportService support, EffectsEngine engine, Func<bool, VirtualCable?> freeCable, Func<bool, string?> busCable)
    {
        _mixer = mixer;
        _support = support;
        _engine = engine;
        _freeCable = freeCable;
        _busCable = busCable;
    }

    public EasyFacts Read()
    {
        if (_support.Current is not { } support) return new EasyFacts { Looking = true };
        var apo = support.Apo;
        var masterChain = _mixer.FxChain(null);
        var masterOn = _mixer.IsFxOn(null);
        var microphones = CombinedDevice.Candidates(_mixer.RecordingDevices).ToList();
        return new EasyFacts
        {
            ApoInstalled = apo.UpstreamInstalled || apo.AnyInstalled,
            CablesReady = EffectsRules.AllowedPairs(support),
            CableInstalled = EffectsRules.CableInstalled(support),
            DeviceSelectorFixes = EffectsRules.DeviceSelectorFixes(apo),
            SpeakersApoWhy = _mixer.MasterDeviceId is { } device ? EffectsRules.ApoMissingOn(apo, apo.PlaybackOf(device)) : EffectsRules.ApoMissing(apo),
            MasterOnBus = _engine.MasterMethod == RunMethod.Bus,
            MasterFxOn = masterOn,
            MasterGains = Effective(masterChain, masterOn),
            MasterStoredGains = EasyEq.StoredGains(masterChain),
            ProgramsWhy = support.ProcessLoopback || support.Cables.Count > 0 ? null : EffectsRules.NeedsCable,
            OwnEqPrograms = _mixer.Settings.Apps.Select(a => a.Key).Where(k => EasyEq.OwnEqOn(_mixer.FxChain(k), _mixer.IsFxOn(k))).ToHashSet(),
            Microphones = microphones.Select(d => new EasyOption(d.Id, d.Name)).ToList(),
            MicGains = _mixer.Settings.Inputs.ToDictionary(i => i.Id, i => Effective(i.Fx, i.FxOn)),
            OwnEqMicrophones = _mixer.Settings.Inputs.Where(i => EasyEq.OwnEqOn(i.Fx, i.FxOn) && !EasyEq.IsFlat(EasyEq.Read(i.Fx))).Select(i => i.Id).ToHashSet(),
            Speakers = CombinedDevice.Candidates(_mixer.Devices).Select(d => new EasyOption(d.Id, d.Name)).ToList(),
            CableForMicrophone = CableFact(support, microphones: true),
            CableForSpeakers = CableFact(support, microphones: false),
            Combined = _mixer.Combined.Select(c => new EasyCombinedFact(c.Key, c.Kind, c.Name, c.Members.ToList(), RecordingNameOf(support, c))).ToList(),
            SuggestedMicrophoneName = EasyApply.FreeName(_mixer.Combined.Select(c => c.Name), "Combined microphone"),
            SuggestedSpeakersName = EasyApply.FreeName(_mixer.Combined.Select(c => c.Name), "Both speakers"),
            MasterPlaysOn = _mixer.MasterPlaysOn.ToList(),
        };
    }

    /// <summary>The five gains a chain makes now: flat while its effects are off.</summary>
    private static int[] Effective(IReadOnlyList<FxSlot> chain, bool effectsOn) => effectsOn ? EasyEq.Read(chain) : EasyEq.Flat();

    private EasyCableFact CableFact(AudioEffectsSupport support, bool microphones)
    {
        var free = _freeCable(microphones);
        var bus = _busCable(microphones);
        var canFree = bus is not null && EffectsRules.MasterOfferOf(EffectsRules.MasterApo, _engine.MasterFacts, _engine.MasterMethod).Available;
        return new EasyCableFact(free?.Name, free?.RecordingName, EffectsRules.NoCableKind(support), bus, canFree);
    }

    /// <summary>What apps pick as a combined microphone: the recording side of the cable it runs on (or last used).</summary>
    private string? RecordingNameOf(AudioEffectsSupport support, CombinedDevice device)
    {
        if (device.Kind != CombinedKind.Microphones) return null;
        var id = _engine.CableOf(device.Key) ?? (device.Cable.Length > 0 ? device.Cable : null);
        return support.Cables.FirstOrDefault(c => c.PlaybackId == id)?.RecordingName;
    }
}
