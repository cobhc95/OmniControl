using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The installed cables not given out yet. A cable pair (<see cref="VirtualCable.PairId"/>: VB-CABLE's "CABLE Input" and "CABLE In
/// 16ch" both come out of "CABLE Output") is ONE cable for every consumer: taking any of its inputs holds them all. Measured on the
/// owner's PC while the bus ran on "CABLE Input": "CABLE In 16ch" could be neither recorded (loopback) nor played into, in any
/// format (0x8889000A, AUDCLNT_E_DEVICE_IN_USE), so a program moved there would lose its sound and its run could not record it.
/// </summary>
internal sealed class CableBox
{
    private readonly IReadOnlyList<VirtualCable> _all;
    private readonly List<VirtualCable> _free;
    private readonly StatusKind _noCable;

    /// <param name="support">What the PC offers: its cables are the ALLOWED ones only (<see cref="SoundModeService.Current"/>), so a
    /// cable the user switched off is never given out.</param>
    public CableBox(AudioEffectsSupport support)
    {
        _all = support.Cables;
        _free = _all.ToList();
        _noCable = SoundModes.NoCableKind(support);
    }

    /// <summary>Every cable input a run uses or holds (a held pair's other inputs too).</summary>
    public List<string> Used { get; } = new();

    /// <summary>Why a consumer got no cable.</summary>
    public string Missing => _all.Count == 0 ? SoundModes.Brief(_noCable) : SoundModes.NeedsAnotherCable;

    /// <summary>The fact behind <see cref="Missing"/>: no cable installed or allowed, or (for a program that chose a cable) none free, else one more is needed.</summary>
    public StatusKind MissingKind(bool program) =>
        _all.Count == 0 ? _noCable : program ? StatusKind.NoFreeCable : StatusKind.NeedsAnotherCable;

    /// <summary>How many cable pairs are installed (VB-CABLE's two inputs are one pair).</summary>
    public int Pairs => _all.Select(c => c.PairId ?? c.PlaybackId).Distinct().Count();

    /// <summary>Whether a whole pair is still free.</summary>
    public bool AnyFree => HasFree(_ => true);

    /// <summary>Whether an endpoint is a cable's recording side (never a combined microphone's member: that would record its own output).</summary>
    public bool IsRecordingSide(string endpointId) => _all.Any(c => c.RecordingId == endpointId || c.PairId == endpointId);

    /// <summary>The short name of <paramref name="playbackId"/>'s cable when apps could record from its pair; else null.</summary>
    public string? RecordableName(string? playbackId) =>
        _all.FirstOrDefault(c => c.PlaybackId == playbackId) is { } cable && _all.Any(c => SamePair(c, cable) && Recordable(c)) ? cable.Name : null;

    /// <summary>
    /// Keeps the whole pair of <paramref name="playbackId"/> from being given out from now on (it carries Windows' default device), without
    /// counting it as used by a run; nothing happens when it is not a cable.
    /// </summary>
    public void Reserve(string? playbackId)
    {
        if (_all.FirstOrDefault(c => c.PlaybackId == playbackId) is { } cable) _free.RemoveAll(c => SamePair(c, cable));
    }

    /// <summary>The remembered cable, while its whole pair is free.</summary>
    public bool TakeRemembered(string cable) =>
        cable.Length > 0 && _free.FirstOrDefault(c => c.PlaybackId == cable && PairFree(c)) is { } found && Take(found);

    /// <summary>The first cable whose whole pair is free and that fits (any when <paramref name="fits"/> is null), or null.</summary>
    public string? TakeAny(Func<VirtualCable, bool>? fits = null) =>
        _free.FirstOrDefault(c => PairFree(c) && (fits?.Invoke(c) ?? true)) is { } found && Take(found) ? found.PlaybackId : null;

    /// <summary>Whether <see cref="TakeAny"/> would find a cable now (nothing is taken).</summary>
    public bool HasFree(Func<VirtualCable, bool> fits) => _free.Any(c => PairFree(c) && fits(c));

    /// <summary>
    /// The first free cable apps can record from, with its whole pair free, for a microphone's or combined microphone's run. Inputs
    /// with no recording side of their own (VB-CABLE's "CABLE In 16ch") are never given to one.
    /// </summary>
    public string? TakeRecordable() =>
        _free.FirstOrDefault(c => Recordable(c) && PairFree(c)) is { } found && Take(found) ? found.PlaybackId : null;

    private static bool Recordable(VirtualCable cable) => cable.RecordingId is not null;

    private static bool SamePair(VirtualCable a, VirtualCable b) => a.PlaybackId == b.PlaybackId || a.PairId is not null && a.PairId == b.PairId;

    private bool PairFree(VirtualCable cable) => _all.Where(c => SamePair(c, cable)).All(_free.Contains);

    private bool Take(VirtualCable cable)
    {
        if (!_free.Remove(cable)) return false;
        Used.Add(cable.PlaybackId);
        foreach (var other in _free.Where(c => SamePair(c, cable)).ToList())
        {
            _free.Remove(other);
            Used.Add(other.PlaybackId);
        }
        return true;
    }
}
