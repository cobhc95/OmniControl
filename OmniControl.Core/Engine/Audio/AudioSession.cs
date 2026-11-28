namespace OmniControl.Core.Engine;

/// Left/right balance maths shared by sessions and the master. Pan runs from -100 (left) to +100 (right).
public static class PanMath
{
    public const int Range = 100;

    public static int Clamp(int pan) => Math.Clamp(pan, -Range, Range);

    /// Channel levels (0-1) for a pan value: the opposite side is turned down, the near side stays at full.
    public static void ToLevels(int pan, out float left, out float right)
    {
        pan = Clamp(pan);
        left = pan > 0 ? 1 - pan / (float)Range : 1;
        right = pan < 0 ? 1 + pan / (float)Range : 1;
    }

    /// The pan value that the given channel levels amount to (0 when they are equal).
    public static int FromLevels(float left, float right)
    {
        if (left <= 0 && right <= 0) return 0;
        if (left < right) return -(int)Math.Round((1 - left / right) * Range);
        if (right < left) return (int)Math.Round((1 - right / left) * Range);
        return 0;
    }
}

/// One audio session (a program's stream on one device). Volume, mute and pan are per session.
public sealed class AudioSession : IDisposable
{
    private readonly CoreAudio.ISimpleAudioVolume _volume;
    private readonly CoreAudio.IChannelAudioVolume? _channels;
    private readonly CoreAudio.IAudioMeterInformation? _meter;
    private readonly CoreAudio.IAudioSessionControl2 _control;
    private bool _disposed;

    internal AudioSession(int pid, string deviceId, int state, CoreAudio.IAudioSessionControl2 control, CoreAudio.ISimpleAudioVolume volume,
        CoreAudio.IChannelAudioVolume? channels, CoreAudio.IAudioMeterInformation? meter)
    {
        ProcessId = pid; DeviceId = deviceId; State = state; _control = control; _volume = volume; _channels = channels; _meter = meter;
    }

    public int ProcessId { get; }
    public string DeviceId { get; }
    /// 0 = open but silent, 1 = playing, as it was when the session was read.
    public int State { get; }
    public bool IsPlaying => State == 1;

    /// Whether the session is playing right now (asked of Windows again; one cheap call, for a quick look between scans).
    public bool IsPlayingNow => _control.GetState(out var state) == 0 ? state == 1 : IsPlaying;

    private static Guid _ctx = Guid.NewGuid();

    public int Volume => _volume.GetMasterVolume(out var v) == 0 ? Percent.FromScalar(v) : Percent.Max;
    public bool Muted => _volume.GetMute(out var m) == 0 && m;
    public void SetVolume(int percent) => _volume.SetMasterVolume(Percent.ToScalar(percent), ref _ctx);
    public void SetMuted(bool muted) => _volume.SetMute(muted, ref _ctx);

    /// The session's volume as Windows holds it (0 to 1, finer than a whole percent). It scales the program's sound linearly (measured:
    /// 1e-4 = exactly -80 dB in the endpoint and in process loopback); used to hold a program near silence while it is recorded.
    public float Scalar => _volume.GetMasterVolume(out var v) == 0 ? v : 1f;
    public void SetScalar(float scalar) => _volume.SetMasterVolume(Math.Clamp(scalar, 0f, 1f), ref _ctx);

    /// The current peak level (0 to 1) of the left and right channels of this program's sound.
    public bool Peaks(out float left, out float right)
    {
        left = right = 0;
        return _meter is not null && CoreAudio.ReadPeaks(_meter, out left, out right);
    }

    /// False when Windows doesn't offer per-channel levels for this session, so pan can't be read or set.
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // All four interfaces are queried from the same underlying COM RCW, so releasing _control releases the session RCW once.
        CoreAudio.ReleaseCom(_control);
    }
}
