using Avalonia.Threading;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// What this PC offers the sound modes (<see cref="SoundModes"/>): Windows' version, the virtual cables, Equalizer APO (on the default
/// playback device and on each microphone). Looked up off the UI thread and kept, and again when the devices change. Reads only: installing is <see cref="EngineInstaller"/>'s job; the chosen mode is stored by
/// <see cref="MixerService"/>.
/// </summary>
public sealed class SoundModeService : IDisposable
{
    private readonly IAudioEffectsSupport _support;
    private readonly MixerService _mixer;
    private readonly ThrottledRun _check;
    /// <summary>The playback and recording devices (and which are default) at the last look, so a change asks for a fresh look.</summary>
    private string _devicesSeen = string.Empty;
    /// <summary>The user's marked cable pairs as of the last request (a copy: the look-up runs off the UI thread).</summary>
    private IReadOnlyList<CableMark> _marks;
    /// <summary>The last look-up as found, every cable pair in it; <see cref="Current"/> is this without the cables switched off.</summary>
    private AudioEffectsSupport? _found;

    /// <summary>Raised on the UI thread when what is available changed.</summary>
    public event Action? Changed;

    public SoundModeService(IAudioEffectsSupport support, MixerService mixer)
    {
        _support = support;
        _mixer = mixer;
        _marks = mixer.MarkedCables.ToList();
        _check = new ThrottledRun(Timing.SoundModeRecheckGap, Check);
        _check.Request();
        mixer.Updated += OnMixerUpdated;
        mixer.CablesChanged += OnCablesChanged;
    }

    /// <summary>A cable switched on or off applies at once; a marked pair added or removed needs a fresh look at the devices.</summary>
    private void OnCablesChanged()
    {
        _marks = _mixer.MarkedCables.ToList();
        Publish(_found);
        _check.Request();
    }

    /// <summary>
    /// A device came or went, or the default changed: Equalizer APO's state is per device (the default playback device, each
    /// microphone), so it is looked at again.
    /// </summary>
    private void OnMixerUpdated()
    {
        var seen = string.Join("|", _mixer.Devices.Concat(_mixer.RecordingDevices).Select(d => d.IsDefault ? "*" + d.Id : d.Id));
        if (seen == _devicesSeen) return;
        _devicesSeen = seen;
        _check.Request();
    }

    /// <summary>The last answer (null until the first look-up finished): its <see cref="AudioEffectsSupport.Cables"/> are the ALLOWED cables only.</summary>
    public AudioEffectsSupport? Current { get; private set; }

    /// <summary>What the modes need to know, or null before the first answer (until then nothing is greyed out or reported missing).</summary>
    public SoundFacts? Facts => Current is { } support
        ? new SoundFacts(support, support.ProcessLoopback && CaptureTestResult.MethodFor(_mixer.Settings.CaptureSilence) is not null,
            CaptureTestResult.IsTested(_mixer.Settings.CaptureSilence))
        : null;

    /// <summary>Asks for a fresh look (the Volume page or the Sound mode panel opened, an install ended); at most once per <see cref="Timing.SoundModeRecheckGap"/>.</summary>
    public void Refresh() => _check.Request();

    /// <summary>
    /// Why a program's effects can't run in the current mode on this PC (Fast mode records the program, and recording does not work
    /// here), or null. The Volume row and the FX window show the same caution.
    /// </summary>
    public string? ProgramCaptureCaution => _mixer.SoundMode == SoundModes.Fast && Facts is { } facts ? SoundModes.CaptureMissing(facts) : null;

    /// <summary>Whether a control works in the current mode (the logic matrix, <see cref="SoundModes.Check"/>).</summary>
    public Gate Check(SoundFeature feature) => SoundModes.Check(feature, _mixer.SoundMode, Facts);

    /// <summary>A mode's plain status on this PC (empty before the first look-up).</summary>
    public IReadOnlyList<ModeStatus> StatusOf(SoundMode mode) => Facts is { } facts ? mode.Status(facts) : Array.Empty<ModeStatus>();

    /// <summary>The capture test result changed (stored in the mixer settings): the modes' status follows.</summary>
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

    /// <summary>What the look-up found, without the cables the user switched off; raises <see cref="Changed"/> when that differs.</summary>
    private void Publish(AudioEffectsSupport? found)
    {
        if (found is null) return;
        var allowed = found with { Cables = VirtualCables.Usable(found.Found, _mixer.ForbiddenCables) };
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
