using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>Carries out the fix a checklist line offered, when the user pressed its button (and answered its question).</summary>
public interface IEasyActions
{
    /// <summary>Runs one fix; returns one calm sentence about how it went (null = nothing to say). The caller reads the state again afterwards.</summary>
    Task<string?> RunAsync(EasyFix fix, EasyAnswers answers, EasyFacts facts);
}

/// <summary>
/// The setup steps that already exist on the Volume page (Equalizer APO's installer and Device Selector, VB-CABLE's installer and
/// page), handed in by the page so Easy runs the very same code, with the same messages, as Advanced's buttons.
/// </summary>
public sealed record EasySetupSteps(Func<Task> InstallApo, Func<Task> ChooseDevices, Func<Task> InstallCable, Action MoreCables);

/// <summary>
/// <see cref="IEasyActions"/> over the real services: the existing setup steps, and <see cref="EasyApply"/> through an
/// <see cref="IEasyMixer"/>. One method per fix id; nothing runs that no button asked for.
/// </summary>
public sealed class EasyActions : IEasyActions
{
    private readonly IEasyMixer _mixer;
    private readonly EasySetupSteps _steps;

    public EasyActions(IEasyMixer mixer, EasySetupSteps steps)
    {
        _mixer = mixer;
        _steps = steps;
    }

    public async Task<string?> RunAsync(EasyFix fix, EasyAnswers answers, EasyFacts facts)
    {
        switch (fix.Id)
        {
            case EasyFixes.InstallApo: await _steps.InstallApo(); return null;
            case EasyFixes.ChooseDevices: await _steps.ChooseDevices(); return null;
            case EasyFixes.InstallCable: await _steps.InstallCable(); return null;
            case EasyFixes.MoreCables: _steps.MoreCables(); return null;
            case EasyFixes.ApplyMasterSound: EasyApply.MasterSound(_mixer); return "Your sound is on.";
            case EasyFixes.ApplyProgramSounds: EasyApply.ProgramSounds(_mixer, answers.Programs); return "Each app has its own sound now.";
            case EasyFixes.ApplyMicrophone: return ApplyMicrophone(answers, facts);
            case EasyFixes.UseOwnEngine or EasyFixes.SwitchToFull: EasyApply.UseOwnEngine(_mixer); return "Full control is on.";
            case EasyFixes.SwitchToFast: EasyApply.UseFastMode(_mixer); return "Fast mode is on.";
            case EasyFixes.FreeCable: EasyApply.FreeTheCable(_mixer); return "The master now runs in Equalizer APO.";
            case EasyFixes.CreateMicrophone: return Create(CombinedKind.Microphones, answers, facts.SuggestedMicrophoneName);
            case EasyFixes.CreateSpeakers: return Create(CombinedKind.Speakers, answers, facts.SuggestedSpeakersName);
            case EasyFixes.PlayOnBoth: return PlayOnBoth(answers, facts);
            default: return "That step is not available.";
        }
    }

    private string? ApplyMicrophone(EasyAnswers answers, EasyFacts facts)
    {
        if (answers.MicrophoneId is not { } id || facts.Microphones.FirstOrDefault(m => m.Id == id) is not { } mic) return "Choose a microphone first.";
        EasyApply.Microphone(_mixer, id, mic.Name, answers.MicGains);
        return EasyEq.IsFlat(answers.MicGains) ? "The microphone is left as it was." : $"{answers.MicLook} is on for {mic.Name}.";
    }

    private string Create(CombinedKind kind, EasyAnswers answers, string suggested)
    {
        var device = EasyApply.Combine(_mixer, kind, answers.Name.Trim().Length > 0 ? answers.Name : suggested, answers.Devices);
        return $"{device.Name} created.";
    }

    private string? PlayOnBoth(EasyAnswers answers, EasyFacts facts)
    {
        if (EasyChecks.CombinedOf(facts, CombinedKind.Speakers, answers) is not { } made) return "Create the combined speaker first.";
        EasyApply.MasterPlaysOn(_mixer, made.Key);
        return $"Your master plays on {made.Name}.";
    }
}
