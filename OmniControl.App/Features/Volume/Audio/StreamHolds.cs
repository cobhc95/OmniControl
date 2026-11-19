namespace OmniControl.App.Features.Volume.Audio;

/// How the original sound of a program is kept quiet while the effects engine plays a processed copy.
public enum SilenceMethod
{
    /// The session is muted (at full volume and centred, so the capture gets the plain sound).
    Mute,
    /// The session's volume is 0 (for PCs where a muted session also silences the capture).
    ZeroVolume,
    /// The session's volume is held at <see cref="QuietLevel.Scalar"/> (-80 dB) and the engine makes the recording up by the same
    /// amount (for PCs where the recording follows the session's volume, as on the owner's: muting or volume 0 silences it). Measured
    /// there: the recording keeps the sound exactly (restored to 0.0 dB), the original plays on 80 dB below the processed copy.
    Quiet,
}

/// The one rule of <see cref="SilenceMethod.Quiet"/>: how far the original is turned down and how much the recording is made up.
public static class QuietLevel
{
    /// The session volume the original is held at: -80 dB, far below hearing under the processed copy, still exact in a float recording.
    public const float Scalar = 1e-4f;

    /// The gain that brings the recording back to the program's own level (1 / <see cref="Scalar"/>).
    public const float Makeup = 1f / Scalar;

    /// A recorded sample above this cannot come from a session held at <see cref="Scalar"/> (that would be a program louder than +12 dBFS):
    /// the session is not turned down (yet, or any more), so that block is left out instead of being made 80 dB louder.
    public const float RawCeiling = 4 * Scalar;

    /// The recording's gain for a silencing method (1 unless the original is only turned down).
    public static float MakeupFor(SilenceMethod method) => method == SilenceMethod.Quiet ? Makeup : 1f;

    /// The ceiling for a silencing method's recording (none unless the original is only turned down).
    public static float CeilingFor(SilenceMethod method) => method == SilenceMethod.Quiet ? RawCeiling : float.PositiveInfinity;

    /// Whether a recording peak taken after the original was silenced shows the capture kept the sound: any sound for mute and volume 0;
    /// for Quiet sound that is also turned down (a recording that stays loud is taken before the session's volume and cannot be made up).
    public static bool KeepsSound(SilenceMethod method, float rawPeak) => rawPeak > 0 && rawPeak <= CeilingFor(method);
}

/// What the effects engine asks of the mixer while it processes a program: keep the program's own sessions silent (the row's
/// volume, pan and mute then live only in the entry and the engine applies them), or keep its output on a virtual cable. The
/// mixer owns the sessions and per-program devices, so it applies these to every stream of the program, including ones that
/// open later, and puts the user's own values back when released. The mixer asks it, at every scan, to keep new streams held too.
public sealed class StreamHolds
{
    private readonly Dictionary<string, SilenceMethod> _silenced = new();
    private readonly RoutingHolds _routed = new();
    private readonly Func<string, List<AudioSession>?> _sessionsOf;
    private readonly Func<string, AppAudioEntry?> _find;
    private readonly Func<string, string?> _directRouteOf;
    private readonly Action<string>? _report;

    /// <param name="sessionsOf">A program's sessions now (null: none).</param>
    /// <param name="find">A program's stored entry: the user's own volume, mute and pan.</param>
    /// <param name="directRouteOf">The device the engine chose for the program (null: the default).</param>
    internal StreamHolds(Func<string, List<AudioSession>?> sessionsOf, Func<string, AppAudioEntry?> find, Func<string, string?> directRouteOf, Action<string>? report)
    {
        _sessionsOf = sessionsOf;
        _find = find;
        _directRouteOf = directRouteOf;
        _report = report;
    }

    public bool IsSilenced(string key) => _silenced.ContainsKey(key);
    public bool IsRoutingHeld(string key) => _routed.IsHeld(key);

    /// The program's open streams right now: process id and playback device of each session.
    public IReadOnlyList<(int ProcessId, string DeviceId)> OpenStreams(string key) =>
        _sessionsOf(key) is { } list ? list.Select(s => (s.ProcessId, s.DeviceId)).ToList() : Array.Empty<(int, string)>();

    /// The loudest live level (0 to 1) of the program's streams on devices other than <paramref name="deviceId"/>: sound a program held
    /// on a cable still plays somewhere else, because Windows has not moved that stream (0 when it plays only there, or not at all).
    public float PeakOutside(string key, string deviceId)
    {
        if (_sessionsOf(key) is not { } list) return 0;
        var peak = 0f;
        foreach (var s in list.Where(s => s.DeviceId != deviceId))
            if (s.Peaks(out var l, out var r)) peak = Math.Max(peak, Math.Max(l, r));
        return peak;
    }

    /// Keeps every stream of the program silent until <see cref="Unsilence"/>.
    public void Silence(string key, SilenceMethod method)
    {
        _silenced[key] = method;
        if (_sessionsOf(key) is { } list) KeepSilenced(key, list);
    }

    /// Puts the user's own volume, mute and pan back on every stream of the program. False when it has no stream now (it closed while
    /// silenced): Windows keeps a closed program's volume and mute, so the caller keeps its journal entry and the next start (or the
    /// program's next stream, which gets its saved values) puts them back.
    public bool Unsilence(string key)
    {
        if (!_silenced.Remove(key) || _find(key) is not { } entry || _sessionsOf(key) is not { Count: > 0 } list) return false;
        foreach (var s in list)
        {
            s.SetVolume(entry.Volume);
            s.SetMuted(entry.Muted);
            s.SetPan(entry.Pan);
        }
        return true;
    }

    /// Applies the silence to sessions that do not have it yet (new streams, or a program that undid it).
    internal void KeepSilenced(string key, List<AudioSession> list)
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

    /// The session volume (0 to 1) a silencing method holds: full under a mute (so the capture gets the plain sound), 0, or <see cref="QuietLevel.Scalar"/>.
    private static float SessionScalar(SilenceMethod method) => method switch
    {
        SilenceMethod.Mute => Percent.ToScalar(Percent.Max),
        SilenceMethod.ZeroVolume => Percent.ToScalar(Percent.Min),
        _ => QuietLevel.Scalar,
    };

    /// Sends every stream of the program (now and later) to <paramref name="deviceId"/> until <see cref="ReleaseRouting"/>. False with
    /// a short reason (no final full stop) when Windows refused.
    public bool HoldRouting(string key, string deviceId, out string error)
    {
        _routed.Hold(key, deviceId);
        error = string.Empty;
        return _sessionsOf(key) is not { } list || CoreAudio.TrySetProgramDevice(list.Select(s => s.ProcessId), deviceId, out error);
    }

    /// Puts the program back on its own device (the one the engine chose, or the default) when it is still held on
    /// <paramref name="deviceId"/>, the caller's cable (see <see cref="RoutingHolds"/>). False when another run holds it now: nothing changes.
    public bool ReleaseRouting(string key, string deviceId)
    {
        if (!_routed.Release(key, deviceId)) return false;
        if (_find(key) is not { } entry || _sessionsOf(key) is not { } list) return true;
        if (!CoreAudio.TrySetProgramDevice(list.Select(s => s.ProcessId), _directRouteOf(key), out var error))
            _report?.Invoke($"Couldn't move {entry.Name} back from the virtual cable: {error}.");
        return true;
    }

    /// A program that opened a new stream while held on a cable goes there too.
    internal void KeepRouted(string key, int pid)
    {
        if (_routed.DeviceOf(key) is { } device && !CoreAudio.TrySetProgramDevice(new[] { pid }, device, out var error))
            _report?.Invoke($"Couldn't send a new stream of {_find(key)?.Name} to the virtual cable: {error}.");
    }
}
