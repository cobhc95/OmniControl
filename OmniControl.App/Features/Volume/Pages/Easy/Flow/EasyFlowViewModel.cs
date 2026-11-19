namespace OmniControl.App.Features.Volume.Pages.Easy.Flow;

/// What the flow needs from the page: the live facts, the carrying out of a fix, and the question a fix asks (true = the user said yes).
public sealed record EasyFlowServices(Func<EasyFacts> Read, IEasyActions Actions, Func<EasyConfirm, Task<bool>> Confirm);

/// A small picture on the Setup screen: its boxes.
public sealed record EasyPictureRow(IReadOnlyList<PictureStep> Steps);

/// One goal's setup as it runs: the screens of its <see cref="EasyFlowDef"/> in order with Back and Next, the progress strip, the
/// questions' answers, and the Setup checklist that is worked out again from the live facts whenever <see cref="Reevaluate"/> is called
/// (the page does that on every change), so lines tick by themselves. Nothing changes on the PC except through a fix button the
/// user pressed (after its question); leaving halfway, or Back, changes nothing that was not already pressed.
public sealed partial class EasyFlowViewModel : ObservableObject, IDisposable
{
    private readonly EasyGoal _goal;
    private readonly EasyFlowDef _def;
    private readonly IReadOnlyList<EasyStep> _steps;
    private readonly Action<EasyEngine>? _engineChosen;
    private readonly EasyAnswers _answers;
    private readonly EasyFlowServices _services;
    private readonly IReadOnlyList<EasyQuestionViewModel?> _questions;
    private readonly Action _toCards;
    private readonly Action _toPage;
    private readonly EasyFlowPlace? _place;
    private EasyFacts _facts = new() { Looking = true };
    private string? _picturesShown;

    public EasyFlowViewModel(EasyGoal goal, EasyFlowDef def, EasyAnswers answers, EasyFlowServices services,
        Func<EasyQuestionKind, Action, EasyQuestionViewModel> makeQuestion, Action toCards, Action toPage, Action<EasyEngine>? engineChosen = null, EasyFlowPlace? place = null)
    {
        _goal = goal;
        _def = def;
        _answers = answers;
        _services = services;
        _toCards = toCards;
        _toPage = toPage;
        _engineChosen = engineChosen;
        _place = place;
        _steps = EasyFlows.StepsOf(goal, def, place?.SkipEngine != true);
        _questions = _steps.Select(s => s.Kind == EasyStepKind.Question ? makeQuestion(s.Question, Reevaluate) : null).ToList();
        Strip = new[] { new EasyStripItem(IsSequence ? $"{goal.Title} (goal {place!.Number} of {place.Count})" : "Goal") }.Concat(_steps.Select(s => new EasyStripItem(s.Title))).ToList();
        EngineCards = new[] { EasyEngine.Apo, EasyEngine.Cable }.Select(e => new EasyEngineItem(e, PickEngine, GetEngineAsync)).ToList();
        BackCommand = new RelayCommand(Back);
        SkipGoalCommand = new RelayCommand(() => _place?.SkipGoal());
        NextGoalCommand = new RelayCommand(() => _place?.NextGoal());
        NextCommand = new RelayCommand(Next, () => CanNext);
        Enter(0);
        Reevaluate();
    }

    public IReadOnlyList<EasyStripItem> Strip { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand NextCommand { get; }

    // ---- one goal of several ----

    /// This setup is one of several (the user ticked more than one goal).
    public bool IsSequence => _place is { IsSequence: true };

    /// Another goal follows this one.
    public bool HasNextGoal => _place is { IsSequence: true, HasNext: true };

    /// The words of the button that leaves a goal that cannot be finished: on to the next goal, or (for the last) finish without it.
    public string SkipLabel => HasNextGoal ? "Continue with the next goal" : "Finish without this goal";

    /// Leaves this goal without applying anything more and goes on (shown in a sequence, on every screen but "All set").
    public IRelayCommand SkipGoalCommand { get; }

    /// The "All set" screen's way on to the next goal.
    public IRelayCommand NextGoalCommand { get; }

    /// The way the user settled on at the Engine screen when the goal offered a real choice (carried to the goals after it); null otherwise.
    public EasyEngine? SettledEngine { get; private set; }

    /// One line saying why this goal cannot be finished now (a line of its checklist is blocked), only inside a sequence; empty otherwise.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStuckNote))] private string _stuckNote = string.Empty;

    public bool HasStuckNote => StuckNote.Length > 0;
    public ObservableCollection<EasyCheckItem> Checks { get; } = new();
    public ObservableCollection<EasyCheckItem> Extras { get; } = new();
    public ObservableCollection<EasyPictureRow> Pictures { get; } = new();

    /// The two cards of the Engine step, in place for good; their words and states are read from the live facts.
    public IReadOnlyList<EasyEngineItem> EngineCards { get; }

    public string EnginePrompt => EasyEngines.Prompt;
    public string EngineHint => EasyEngines.Hint;

    /// Why the Engine step cannot be left yet (the chosen way is not installed); empty when it can.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEngineProblem))] private string _engineProblem = string.Empty;

    public bool HasEngineProblem => EngineProblem.Length > 0;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsQuestion), nameof(IsEngine), nameof(IsSetup), nameof(IsDone), nameof(Question))] private int _stepIndex;

    private EasyStep Step => _steps[StepIndex];
    public bool IsQuestion => Step.Kind == EasyStepKind.Question;
    public bool IsEngine => Step.Kind == EasyStepKind.Engine;
    public bool IsSetup => Step.Kind == EasyStepKind.Setup;
    public bool IsDone => Step.Kind == EasyStepKind.Done;
    public EasyQuestionViewModel? Question => _questions[StepIndex];

    /// Whether Next can be pressed: a question is answered enough, or every required line of the checklist is done.
    [ObservableProperty] private bool _canNext;

    /// One calm sentence about what the last pressed step did; cleared when the screen changes.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    [ObservableProperty] private string _doneHeadline = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _doneLines = Array.Empty<string>();

    /// Why Next is dimmed, as the button's tooltip (empty = it can be pressed).
    [ObservableProperty] private string _nextTip = string.Empty;

    // ---- moving between screens ----

    private void Back()
    {
        if (StepIndex == 0)
        {
            _toCards();
            return;
        }
        Enter(StepIndex - 1);
        Reevaluate();
    }

    private void Next()
    {
        if (!CanNext || StepIndex >= _steps.Count - 1) return;
        if (IsEngine && _goal.Engine is { HasChoice: true }) SettledEngine = _answers.Engine;
        Enter(StepIndex + 1);
        Reevaluate();
    }

    [RelayCommand] private void GoToVolume() => _toPage();

    [RelayCommand] private void SetUpSomethingElse() => _toCards();

    private void Enter(int index)
    {
        Question?.Show(false);
        Note = string.Empty;
        StepIndex = index;
        Question?.Show(true);
        for (var i = 0; i < Strip.Count; i++)
        {
            Strip[i].IsDone = i <= index;
            Strip[i].IsCurrent = i == index + 1;
        }
    }

    // ---- the live checklist ----

    /// Reads the PC again and redraws what depends on it: the choices, the checklist's ticks, the "All set" words, the Next button.
    public void Reevaluate()
    {
        _facts = _services.Read();
        foreach (var question in _questions) question?.Refresh(_facts);
        if (_goal.Engine is { } rule && !_facts.Looking) SyncEngine(rule);
        if (IsSetup)
        {
            Sync(Checks, Evaluate(_facts));
            SyncPictures();
        }
        StuckNote = IsSequence && IsSetup && !_facts.Looking && Checks.FirstOrDefault(StuckLine) is { } stuck ? $"{stuck.Title}: {stuck.Subtitle}" : string.Empty;
        if (IsDone)
        {
            Sync(Extras, _def.Extras?.Invoke(_answers, _facts) ?? Array.Empty<EasyCheck>());
            var done = _def.Done(_answers, _facts);
            (DoneHeadline, DoneLines) = (done.Headline, done.Lines);
        }
        (CanNext, NextTip) = Step.Kind switch
        {
            EasyStepKind.Question => Question?.Problem is { Length: > 0 } why ? (false, why) : (true, string.Empty),
            EasyStepKind.Engine => EngineProblem.Length > 0 ? (false, EngineProblem) : (true, string.Empty),
            EasyStepKind.Setup => Checks.Where(c => !c.IsOptional).All(c => c.IsDone) && !_facts.Looking ? (true, string.Empty) : (false, "Finish the steps first"),
            _ => (false, string.Empty),
        };
        NextCommand.NotifyCanExecuteChanged();
    }

    // ---- the Engine step ----

    /// Settles the way (the user's pick, the remembered one, or the pre-selected one) and redraws the two cards and the reason Next waits.
    private void SyncEngine(EasyEngineRule rule)
    {
        var engine = EasyEngines.Resolve(_answers.Engine, rule, _facts);
        _answers.Engine = engine;
        var cards = EasyEngines.Cards(rule, _facts);
        for (var i = 0; i < cards.Count; i++) EngineCards[i].Update(cards[i], cards[i].Engine == engine);
        EngineProblem = EasyEngines.NextWhy(engine, _facts) ?? string.Empty;
    }

    private void PickEngine(EasyEngine engine)
    {
        _answers.Engine = engine;
        _engineChosen?.Invoke(engine);
        Reevaluate();
    }

    /// A card's "Get ..." button: the same installer step the checklists use, run only when pressed; the cards read the PC again afterwards.
    private async Task GetEngineAsync(EasyEngine engine)
    {
        var card = EasyEngines.Cards(_goal.Engine!, _services.Read()).First(c => c.Engine == engine);
        if (!card.CanGet || card.Get is not { } fix) return;
        foreach (var item in EngineCards) item.IsBusy = true;
        try
        {
            Note = await _services.Actions.RunAsync(fix, _answers, _facts) ?? string.Empty;
        }
        finally
        {
            foreach (var item in EngineCards) item.IsBusy = false;
        }
        Reevaluate();
    }

    private IReadOnlyList<EasyCheck> Evaluate(EasyFacts facts) => facts.Looking
        ? new[] { new EasyCheck("looking", "Looking at your PC", "One moment.", CheckState.Blocked) }
        : _def.Checks(_answers, facts);

    private void Sync(ObservableCollection<EasyCheckItem> items, IReadOnlyList<EasyCheck> checks) => EasyCheckItem.Sync(items, checks, RunFixAsync);

    /// A required line that cannot be done now for its own reason (not one that only waits for the lines above it).
    private static bool StuckLine(EasyCheckItem line) => !line.IsOptional && line.IsBlocked && line.Subtitle != EasyChecks.Waiting;

    /// The small pictures above the checklist: the goal's own, or for "A different sound per app" one per ticked app.
    private void SyncPictures()
    {
        var names = _goal.Id == EasyGoals.PerApp
            ? _answers.Programs.Select(key => _facts.Programs.FirstOrDefault(p => p.Id == key)?.Name ?? key).ToList()
            : new List<string> { string.Empty };
        var shown = string.Join("|", names);
        if (shown == _picturesShown) return;
        _picturesShown = shown;
        Pictures.Clear();
        foreach (var name in names)
            Pictures.Add(new EasyPictureRow(PictureStep.Of(name.Length == 0 ? _goal.Picture : _goal.Picture.Select((p, i) => i == 0 ? p with { Text = name } : p).ToList())));
    }

    /// A fix button was pressed: the line is looked at again first (it may have been done or blocked meanwhile), its question is asked
    /// when it has one, and only a yes carries the fix out. The outcome is one calm sentence under the list.
    private async Task RunFixAsync(string checkId)
    {
        var facts = _services.Read();
        var lines = IsDone ? _def.Extras?.Invoke(_answers, facts) ?? Array.Empty<EasyCheck>() : Evaluate(facts);
        if (lines.FirstOrDefault(l => l.Id == checkId) is not { State: CheckState.Todo, Fix: { } fix }) return;
        if (fix.Confirm is { } question && !await _services.Confirm(question)) return;
        SetBusy(true);
        try
        {
            Note = await _services.Actions.RunAsync(fix, _answers, facts) ?? string.Empty;
        }
        finally
        {
            SetBusy(false);
        }
        Reevaluate();
    }

    private void SetBusy(bool busy)
    {
        foreach (var item in Checks.Concat(Extras)) item.IsBusy = busy;
    }

    public void Dispose()
    {
        foreach (var question in _questions) question?.Dispose();
    }
}
