using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>What the user chose on the question screens of one setup. Kept only while the setup is open; nothing here is stored.</summary>
public sealed class EasyAnswers
{
    /// <summary>How the sound is processed (the Engine step): chosen by the user or pre-selected; null until the step has looked at the PC.</summary>
    public EasyEngine? Engine { get; set; }

    /// <summary>The ticked apps (their mixer keys).</summary>
    public HashSet<string> Programs { get; } = new();

    /// <summary>The one microphone to improve (an endpoint id), or null.</summary>
    public string? MicrophoneId { get; set; }

    /// <summary>The look chosen for it, one of <see cref="EasyEq.MicPresets"/> by name.</summary>
    public string MicLook { get; set; } = EasyEq.MicPresets[1].Name;

    /// <summary>The devices ticked to combine (endpoint ids), in the order ticked.</summary>
    public List<string> Devices { get; } = new();

    /// <summary>The name typed for a combined device; empty = the suggested one.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The five gains of the chosen microphone look.</summary>
    public IReadOnlyList<int> MicGains => (EasyEq.MicPresets.FirstOrDefault(p => p.Name == MicLook) ?? EasyEq.MicPresets[0]).Gains;
}
