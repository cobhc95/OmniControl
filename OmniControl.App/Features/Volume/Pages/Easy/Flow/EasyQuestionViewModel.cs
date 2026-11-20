using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages.Easy.Flow;

/// One question screen of a setup: its words, a ticked list of what can be chosen (apps, microphones or speakers), the look chips of a
/// microphone, a name box, or the Sound drawer, as the kind asks. It writes the answers into <see cref="EasyAnswers"/> and says when
/// they changed; what is allowed to continue is <see cref="EasyChecks.Why"/>. The choices are read from the facts each time they change.
public sealed partial class EasyQuestionViewModel : ObservableObject, IDisposable
{
    private readonly EasyQuestionKind _kind;
    private readonly EasyAnswers _answers;
    private readonly Action _changed;
    private string _signature = string.Empty;
    private IReadOnlyList<EasyOption> _options = Array.Empty<EasyOption>();

    /// <param name="sound">The Sound drawer of the master for the Sound question; null for the others.</param>
    /// <param name="addProgram">The Mixer's own "Add program" pop-down (the Apps question); null when the page has none.</param>
    public EasyQuestionViewModel(EasyQuestionKind kind, EasyAnswers answers, Action changed, EasySound? sound, AddProgramMenuViewModel? addProgram)
    {
        _kind = kind;
        _answers = answers;
        _changed = changed;
        Sound = sound;
        AddProgram = addProgram;
        Choices = new OptionListModel(Toggle);
        Looks = EasyEq.MicPresets.Select(p => new ChipItem(p.Name, new RelayCommand(() => PickLook(p.Name)), null)).ToList();
        SyncLooks();
    }

    public OptionListModel Choices { get; }
    public EasySound? Sound { get; }
    public AddProgramMenuViewModel? AddProgram { get; }
    public IReadOnlyList<ChipItem> Looks { get; }

    public bool ShowsChoices => _kind is EasyQuestionKind.Programs or EasyQuestionKind.Microphone or EasyQuestionKind.Microphones or EasyQuestionKind.Speakers;
    public bool ShowsAddProgram => _kind == EasyQuestionKind.Programs && AddProgram is not null;
    public bool ShowsName => _kind is EasyQuestionKind.Microphones or EasyQuestionKind.Speakers;
    public bool ShowsLooks => _kind == EasyQuestionKind.MicLook;
    public bool ShowsSound => _kind == EasyQuestionKind.Sound;

    public string Prompt => _kind switch
    {
        EasyQuestionKind.Sound => "How should it sound?",
        EasyQuestionKind.Programs => "Which apps?",
        EasyQuestionKind.Microphone => "Which microphone?",
        EasyQuestionKind.MicLook => "How should your voice sound?",
        EasyQuestionKind.Microphones => "Which microphones?",
        _ => "Which speakers?",
    };

    public string Hint => _kind switch
    {
        EasyQuestionKind.Sound => "Everything you play changes as you choose.",
        EasyQuestionKind.Programs => "Each ticked app gets its own sound. Playing apps are marked.",
        EasyQuestionKind.Microphone => "The microphone to improve.",
        EasyQuestionKind.MicLook => "An equaliser cleanup of your voice. It does not remove noise.",
        EasyQuestionKind.Microphones => "They are mixed into one microphone.",
        _ => "They play the same sound together.",
    };

    /// The name a new combined device gets when none is typed; shown as the box's watermark.
    [ObservableProperty] private string _namePlaceholder = string.Empty;

    /// The name typed for the combined device; empty = the suggested one.
    public string Name
    {
        get => _answers.Name;
        set
        {
            if (_answers.Name == value) return;
            _answers.Name = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// Why the user cannot go on yet, as a plain sentence; empty when the answer is enough.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem))] private string _problem = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    /// Reads the choices and the suggested name again (the facts changed).
    public void Refresh(EasyFacts facts)
    {
        _options = _kind switch
        {
            EasyQuestionKind.Programs => facts.Programs,
            EasyQuestionKind.Microphone or EasyQuestionKind.Microphones => facts.Microphones,
            EasyQuestionKind.Speakers => facts.Speakers,
            _ => Array.Empty<EasyOption>(),
        };
        var placeholder = _kind == EasyQuestionKind.Microphones ? facts.SuggestedMicrophoneName : facts.SuggestedSpeakersName;
        NamePlaceholder = placeholder;
        var signature = string.Join("|", _options.Select(o => $"{o.Id}={o.Name}{o.Tag}"));
        if (signature != _signature)
        {
            _signature = signature;
            Choices.SetItems(_options.Select(o => new OptionEntry(o.Id, o.Tag.Length > 0 ? $"{o.Name} ({o.Tag})" : o.Name)));
        }
        SyncTicks();
        Problem = EasyChecks.Why(_kind, _answers, facts) ?? string.Empty;
    }

    private void Toggle(OptionEntry entry)
    {
        switch (_kind)
        {
            case EasyQuestionKind.Programs:
                if (!_answers.Programs.Remove(entry.Id)) _answers.Programs.Add(entry.Id);
                break;
            case EasyQuestionKind.Microphone:
                _answers.MicrophoneId = entry.Id;
                break;
            default:
                if (!_answers.Devices.Remove(entry.Id)) _answers.Devices.Add(entry.Id);
                break;
        }
        SyncTicks();
        _changed();
    }

    private void SyncTicks()
    {
        foreach (var entry in Choices.Shown)
            entry.IsChecked = _kind switch
            {
                EasyQuestionKind.Programs => _answers.Programs.Contains(entry.Id),
                EasyQuestionKind.Microphone => _answers.MicrophoneId == entry.Id,
                _ => _answers.Devices.Contains(entry.Id),
            };
    }

    private void PickLook(string look)
    {
        _answers.MicLook = look;
        SyncLooks();
        _changed();
    }

    private void SyncLooks()
    {
        foreach (var chip in Looks) chip.IsSelected = chip.Name == _answers.MicLook;
    }

    /// The question came on screen or left it: the Sound drawer's curve runs only while it is shown.
    public void Show(bool shown)
    {
        if (Sound is not null) Sound.IsOpen = shown;
    }

    public void Dispose() => Sound?.Dispose();
}
