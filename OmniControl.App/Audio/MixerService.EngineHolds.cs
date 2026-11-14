using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Audio;

/// <summary>How the original sound of a program is kept quiet while the effects engine plays a processed copy.</summary>
public enum SilenceMethod
{
    /// <summary>The session is muted (at full volume and centred, so the capture gets the plain sound).</summary>
    Mute,
    /// <summary>The session's volume is 0 (for PCs where a muted session also silences the capture).</summary>
    ZeroVolume,
    /// <summary>
    /// The session's volume is held at <see cref="QuietLevel.Scalar"/> (-80 dB) and the engine makes the recording up by the same
    /// amount (for PCs where the recording follows the session's volume, as on the owner's: muting or volume 0 silences it). Measured
    /// there: the recording keeps the sound exactly (restored to 0.0 dB), the original plays on 80 dB below the processed copy.
    /// </summary>
    Quiet,
}

/// <summary>The one rule of <see cref="SilenceMethod.Quiet"/>: how far the original is turned down and how much the recording is made up.</summary>
public static class QuietLevel
{
    /// <summary>The session volume the original is held at: -80 dB, far below hearing under the processed copy, still exact in a float recording.</summary>
    public const float Scalar = 1e-4f;

    /// <summary>The gain that brings the recording back to the program's own level (1 / <see cref="Scalar"/>).</summary>
    public const float Makeup = 1f / Scalar;

    /// <summary>
    /// A recorded sample above this cannot come from a session held at <see cref="Scalar"/> (that would be a program louder than +12 dBFS):
    /// the session is not turned down (yet, or any more), so that block is left out instead of being made 80 dB louder.
    /// </summary>
    public const float RawCeiling = 4 * Scalar;

    /// <summary>The recording's gain for a silencing method (1 unless the original is only turned down).</summary>
    public static float MakeupFor(SilenceMethod method) => method == SilenceMethod.Quiet ? Makeup : 1f;

    /// <summary>The ceiling for a silencing method's recording (none unless the original is only turned down).</summary>
    public static float CeilingFor(SilenceMethod method) => method == SilenceMethod.Quiet ? RawCeiling : float.PositiveInfinity;

    /// <summary>
    /// Whether a recording peak taken after the original was silenced shows the capture kept the sound: any sound for mute and volume 0;
    /// for Quiet sound that is also turned down (a recording that stays loud is taken before the session's volume and cannot be made up).
    /// </summary>
    public static bool KeepsSound(SilenceMethod method, float rawPeak) => rawPeak > 0 && rawPeak <= CeilingFor(method);
}

/// <summary>
/// What the effects engine asks of the mixer while it processes a program: keep the program's own sessions silent (the row's
/// volume, pan and mute then live only in the entry and the engine applies them), or keep its output on a virtual cable. The
/// mixer owns the sessions and per-program devices, so it applies these to every stream of the program, including ones that
/// open later, and puts the user's own values back when released.
/// </summary>
public sealed partial class MixerService
{
    private readonly Dictionary<string, SilenceMethod> _silenced = new();
    private readonly RoutingHolds _routed = new();

    public bool IsSilenced(string key) => _silenced.ContainsKey(key);
    public bool IsRoutingHeld(string key) => _routed.IsHeld(key);

    /// <summary>The program's open streams right now: process id and playback device of each session.</summary>
    public IReadOnlyList<(int ProcessId, string DeviceId)> OpenStreams(string key) =>
        _sessions.TryGetValue(key, out var list) ? list.Select(s => (s.ProcessId, s.DeviceId)).ToList() : Array.Empty<(int, string)>();

    /// <summary>
    /// The loudest live level (0 to 1) of the program's streams on devices other than <paramref name="deviceId"/>: sound a program held
    /// on a cable still plays somewhere else, because Windows has not moved that stream (0 when it plays only there, or not at all).
    /// </summary>
    public float PeakOutside(string key, string deviceId)
    {
        if (!_sessions.TryGetValue(key, out var list)) return 0;
        var peak = 0f;
        foreach (var s in list.Where(s => s.DeviceId != deviceId))
            if (s.Peaks(out var l, out var r)) peak = Math.Max(peak, Math.Max(l, r));
        return peak;
    }

    /// <summary>Keeps every stream of the program silent until <see cref="Unsilence"/>.</summary>
    public void Silence(string key, SilenceMethod method)
    {
        _silenced[key] = method;
        if (_sessions.TryGetValue(key, out var list)) KeepSilenced(key, list);
    }

    /// <summary>
    /// Puts the user's own volume, mute and pan back on every stream of the program. False when it has no stream now (it closed while
    /// silenced): Windows keeps a closed program's volume and mute, so the caller keeps its journal entry and the next start (or the
    /// program's next stream, which gets its saved values) puts them back.
    /// </summary>
    public bool Unsilence(string key)
    {
        if (!_silenced.Remove(key) || Find(key) is not { } entry || !_sessions.TryGetValue(key, out var list) || list.Count == 0) return false;
        foreach (var s in list)
        {
            s.SetVolume(entry.Volume);
            s.SetMuted(entry.Muted);
            s.SetPan(entry.Pan);
        }
        return true;
    }

    /// <summary>Applies the silence to sessions that do not have it yet (new streams, or a program that undid it).</summary>
    private void KeepSilenced(string key, List<AudioSession> list)
    {
        if (!_silenced.TryGetValue(key, out var method)) return;
        var scalar = SessionScalar(method);
        var muted = method == SilenceMethod.Mute;
        foreach (var s in list.Where(s => Math.Abs(s.Scalar - scalar) > QuietLevel.Scalar / 2 || s.Muted != muted))
        {
            s.SetScalar(scalar);
            s.SetMuted(muted);
            s.SetPan(0);
        }
    }

    /// <summary>The session volume (0 to 1) a silencing method holds: full under a mute (so the capture gets the plain sound), 0, or <see cref="QuietLevel.Scalar"/>.</summary>
    private static float SessionScalar(SilenceMethod method) => method switch
    {
        SilenceMethod.Mute => Percent.ToScalar(Percent.Max),
        SilenceMethod.ZeroVolume => Percent.ToScalar(Percent.Min),
        _ => QuietLevel.Scalar,
    };

    /// <summary>
    /// Sends every stream of the program (now and later) to <paramref name="deviceId"/> until <see cref="ReleaseRouting"/>. False with
    /// a short reason (no final full stop) when Windows refused.
    /// </summary>
    public bool HoldRouting(string key, string deviceId, out string error)
    {
        _routed.Hold(key, deviceId);
        error = string.Empty;
        return !_sessions.TryGetValue(key, out var list) || CoreAudio.TrySetProgramDevice(list.Select(s => s.ProcessId), deviceId, out error);
    }

    /// <summary>
    /// Puts the program back on its own device (<see cref="DirectRouteOf"/>, or the default) when it is still held on
    /// <paramref name="deviceId"/>, the caller's cable (see <see cref="RoutingHolds"/>). False when another run holds it now: nothing changes.
    /// </summary>
    public bool ReleaseRouting(string key, string deviceId)
    {
        if (!_routed.Release(key, deviceId)) return false;
        if (Find(key) is not { } entry || !_sessions.TryGetValue(key, out var list)) return true;
        if (!CoreAudio.TrySetProgramDevice(list.Select(s => s.ProcessId), DirectRouteOf(key), out var error))
            Report?.Invoke($"Couldn't move {entry.Name} back from the virtual cable: {error}.");
        return true;
    }

    /// <summary>A program that opened a new stream while held on a cable goes there too.</summary>
    private void KeepRouted(string key, int pid)
    {
        if (_routed.DeviceOf(key) is { } device && !CoreAudio.TrySetProgramDevice(new[] { pid }, device, out var error))
            Report?.Invoke($"Couldn't send a new stream of {Find(key)?.Name} to the virtual cable: {error}.");
    }
}
