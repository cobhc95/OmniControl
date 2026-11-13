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
    private readonly Dictionary<string, string> _routed = new();

    public bool IsSilenced(string key) => _silenced.ContainsKey(key);
    public bool IsRoutingHeld(string key) => _routed.ContainsKey(key);

    /// <summary>The program's open streams right now: process id and playback device of each session.</summary>
    public IReadOnlyList<(int ProcessId, string DeviceId)> OpenStreams(string key) =>
        _sessions.TryGetValue(key, out var list) ? list.Select(s => (s.ProcessId, s.DeviceId)).ToList() : Array.Empty<(int, string)>();

    /// <summary>Keeps every stream of the program silent until <see cref="Unsilence"/>.</summary>
    public void Silence(string key, SilenceMethod method)
    {
        _silenced[key] = method;
        if (_sessions.TryGetValue(key, out var list)) KeepSilenced(key, list);
    }

    /// <summary>Puts the user's own volume, mute and pan back on every stream of the program.</summary>
    public void Unsilence(string key)
    {
        if (!_silenced.Remove(key) || Find(key) is not { } entry || !_sessions.TryGetValue(key, out var list)) return;
        foreach (var s in list)
        {
            s.SetVolume(entry.Volume);
            s.SetMuted(entry.Muted);
            s.SetPan(entry.Pan);
        }
    }

    /// <summary>Applies the silence to sessions that do not have it yet (new streams, or a program that undid it).</summary>
    private void KeepSilenced(string key, List<AudioSession> list)
    {
        if (!_silenced.TryGetValue(key, out var method)) return;
        var volume = method == SilenceMethod.Mute ? Percent.Max : Percent.Min;
        var muted = method == SilenceMethod.Mute;
        foreach (var s in list.Where(s => s.Volume != volume || s.Muted != muted))
        {
            s.SetVolume(volume);
            s.SetMuted(muted);
            s.SetPan(0);
        }
    }

    /// <summary>Sends every stream of the program (now and later) to <paramref name="deviceId"/> until <see cref="ReleaseRouting"/>.</summary>
    public bool HoldRouting(string key, string deviceId, out string error)
    {
        _routed[key] = deviceId;
        error = string.Empty;
        if (!_sessions.TryGetValue(key, out var list)) return true;
        foreach (var pid in list.Select(s => s.ProcessId).Distinct())
            if (!CoreAudio.TrySetAppDevice(pid, deviceId, out error)) return false;
        return true;
    }

    /// <summary>Puts the program back on its own device (<see cref="DirectRouteOf"/>, or the default).</summary>
    public void ReleaseRouting(string key)
    {
        if (!_routed.Remove(key) || Find(key) is not { } entry || !_sessions.TryGetValue(key, out var list)) return;
        foreach (var pid in list.Select(s => s.ProcessId).Distinct())
            if (!CoreAudio.TrySetAppDevice(pid, DirectRouteOf(key), out var error))
                Report?.Invoke($"Couldn't move {entry.Name} back from the virtual cable: {error}");
    }

    /// <summary>A program that opened a new stream while held on a cable goes there too.</summary>
    private void KeepRouted(string key, int pid)
    {
        if (_routed.TryGetValue(key, out var device) && !CoreAudio.TrySetAppDevice(pid, device, out var error))
            Report?.Invoke($"Couldn't send a new stream of {Find(key)?.Name} to the virtual cable: {error}");
    }
}
