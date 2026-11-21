using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Audio;

/// What this PC offers the effects engine (<see cref="EffectsRules"/>): Windows' version, the virtual cables, Equalizer APO (on the default
/// playback device and on each microphone). Looked up off the UI thread and kept, and again when the devices change. Reads only:
/// installing is <see cref="EngineInstaller"/>'s job.
public sealed class EffectsSupportService : IDisposable
{
    private readonly IAudioEffectsSupport _support;
    private readonly MixerService _mixer;
    private readonly ThrottledRun _check;
    /// The playback and recording devices (and which are default) at the last look, so a change asks for a fresh look.
    private string _devicesSeen = string.Empty;
    /// The user's marked cable pairs as of the last request (a copy: the look-up runs off the UI thread).
    private IReadOnlyList<CableMark> _marks;
    /// The last look-up as found, every cable pair in it; <see cref="Current"/> is this without the cables switched off.
    private AudioEffectsSupport? _found;

    /// Raised on the UI thread when what is available changed.
    public event Action? Changed;

    public EffectsSupportService(IAudioEffectsSupport support, MixerService mixer)
    {
        _support = support;
        _mixer = mixer;
        _marks = mixer.MarkedCables.ToList();
        _check = new ThrottledRun(Timing.SupportRecheckGap, Check);
        _check.Request();
        mixer.Updated += OnMixerUpdated;
        mixer.CablesChanged += OnCablesChanged;
    }

    /// A cable switched on or off applies at once; a marked pair added or removed needs a fresh look at the devices.
    private void OnCablesChanged()
    {
        _marks = _mixer.MarkedCables.ToList();
        Publish(_found);
        _check.Request();
    }

    /// A device came or went, or the default changed: Equalizer APO's state is per device (the default playback device, each
    /// microphone), so it is looked at again.
    private void OnMixerUpdated()
    {
        var seen = string.Join("|", _mixer.Devices.Concat(_mixer.RecordingDevices).Select(d => d.IsDefault ? "*" + d.Id : d.Id));
        if (seen == _devicesSeen) return;
        _devicesSeen = seen;
        _check.Request();
    }

    /// The last answer (null until the first look-up finished): its <see cref="AudioEffectsSupport.Cables"/> are the ALLOWED cables only.
    public AudioEffectsSupport? Current { get; private set; }

    /// What the engine needs to know, or null before the first answer (until then nothing is greyed out or reported missing).
    public SoundFacts? Facts => Current is { } support
        ? new SoundFacts(support, support.ProcessLoopback && CaptureTestResult.MethodFor(_mixer.Settings.CaptureSilence) is not null)
        : null;

    /// Asks for a fresh look (the Volume page opened, an install ended); at most once per <see cref="Timing.SupportRecheckGap"/>.
    public void Refresh() => _check.Request();

    /// The cable status on this PC (null before the first look-up).
    public TagItem? CableStatus => Facts is { } facts ? EffectsRules.CableStatus(facts) : null;

    /// The capture test result changed (stored in the mixer settings): what the engine may do follows.
    public void NotifyTested() => Changed?.Invoke();

    private void Check()
    {
        var found = _support.Check(_marks);
        Dispatcher.UIThread.Post(() =>
        {
            _found = found;
            Publish(found);
        });
    }

    /// What the look-up found, with only the cables the user switched on; raises <see cref="Changed"/> when that differs.
    private void Publish(AudioEffectsSupport? found)
    {
        if (found is null) return;
        _mixer.KeepEarlierCables(found.Found);
        var off = found.Found.Where(f => !_mixer.IsCableAllowed(f.Id)).Select(f => f.Id).ToHashSet();
        var allowed = found with { Cables = VirtualCables.Usable(found.Found, off) };
        if (allowed.Equals(Current)) return;
        Current = allowed;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _mixer.Updated -= OnMixerUpdated;
        _mixer.CablesChanged -= OnCablesChanged;
        _check.Dispose();
    }
}
