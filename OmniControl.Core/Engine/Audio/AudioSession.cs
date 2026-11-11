namespace OmniControl.Core.Engine;

/// <summary>Left/right balance maths shared by sessions and the master. Pan runs from -100 (left) to +100 (right).</summary>
public static class PanMath
{
    /// <summary>Channel levels (0-1) for a pan value: the opposite side is turned down, the near side stays at full.</summary>
    public static void ToLevels(int pan, out float left, out float right)
    {
        pan = Math.Clamp(pan, -100, 100);
        left = pan > 0 ? 1 - pan / 100f : 1;
        right = pan < 0 ? 1 + pan / 100f : 1;
    }

    /// <summary>The pan value that the given channel levels amount to (0 when they are equal).</summary>
    public static int FromLevels(float left, float right)
    {
        if (left <= 0 && right <= 0) return 0;
        if (left < right) return -(int)Math.Round((1 - left / right) * 100);
        if (right < left) return (int)Math.Round((1 - right / left) * 100);
        return 0;
    }
}

/// <summary>One audio session (a program's stream on one device). Volume, mute and pan are per session.</summary>
public sealed class AudioSession
{
    private readonly CoreAudio.ISimpleAudioVolume _volume;
    private readonly CoreAudio.IChannelAudioVolume? _channels;
    private readonly CoreAudio.IAudioMeterInformation? _meter;

    internal AudioSession(int pid, string deviceId, int state, CoreAudio.ISimpleAudioVolume volume, CoreAudio.IChannelAudioVolume? channels,
        CoreAudio.IAudioMeterInformation? meter)
    {
        ProcessId = pid; DeviceId = deviceId; State = state; _volume = volume; _channels = channels; _meter = meter;
    }

    public int ProcessId { get; }
    public string DeviceId { get; }
    /// <summary>0 = open but silent, 1 = playing.</summary>
    public int State { get; }
    public bool IsPlaying => State == 1;

    private static Guid _ctx = Guid.NewGuid();

    public int Volume => _volume.GetMasterVolume(out var v) == 0 ? (int)Math.Round(v * 100) : 100;
    public bool Muted => _volume.GetMute(out var m) == 0 && m;
    public void SetVolume(int percent) => _volume.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref _ctx);
    public void SetMuted(bool muted) => _volume.SetMute(muted, ref _ctx);

    /// <summary>The current peak level (0 to 1) of the left and right channels of this program's sound.</summary>
    public bool Peaks(out float left, out float right)
    {
        left = right = 0;
        return _meter is not null && CoreAudio.ReadPeaks(_meter, out left, out right);
    }

    /// <summary>False when Windows doesn't offer per-channel levels for this session, so pan can't be read or set.</summary>
    public bool CanPan => _channels is not null;

    public int Pan
    {
        get
        {
            if (_channels is null || _channels.GetChannelCount(out var n) != 0 || n < 2) return 0;
            if (_channels.GetChannelVolume(0, out var l) != 0 || _channels.GetChannelVolume(1, out var r) != 0) return 0;
            return PanMath.FromLevels(l, r);
        }
    }

    public void SetPan(int pan)
    {
        if (_channels is null || _channels.GetChannelCount(out var n) != 0 || n < 2) return;
        PanMath.ToLevels(pan, out var l, out var r);
        _channels.SetChannelVolume(0, l, ref _ctx);
        _channels.SetChannelVolume(1, r, ref _ctx);
    }
}
