using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages.Easy.Flow;

/// What the user chose on the question screens of one setup. Kept only while the setup is open; nothing here is stored.
public sealed class EasyAnswers
{
    /// How the sound is processed (the Engine step): chosen by the user or pre-selected; null until the step has looked at the PC.
    public EasyEngine? Engine { get; set; }

    /// The ticked apps (their mixer keys).
    public HashSet<string> Programs { get; } = new();

    /// The one microphone to improve (an endpoint id), or null.
    public string? MicrophoneId { get; set; }

    /// The look chosen for it, one of <see cref="EasyEq.MicPresets"/> by name.
    public string MicLook { get; set; } = EasyEq.MicPresets[1].Name;

    /// The devices ticked to combine (endpoint ids), in the order ticked.
    public List<string> Devices { get; } = new();

    /// The name typed for a combined device; empty = the suggested one.
    public string Name { get; set; } = string.Empty;

    /// The five gains of the chosen microphone look.
    public IReadOnlyList<int> MicGains => (EasyEq.MicPresets.FirstOrDefault(p => p.Name == MicLook) ?? EasyEq.MicPresets[0]).Gains;
}
