using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>What the flow needs from the page: the live facts, the carrying out of a fix, and the question a fix asks (true = the user said yes).</summary>
public sealed record EasyFlowServices(Func<EasyFacts> Read, IEasyActions Actions, Func<EasyConfirm, Task<bool>> Confirm);

/// <summary>A small picture on the Setup screen: its boxes.</summary>
public sealed record EasyPictureRow(IReadOnlyList<PictureStep> Steps);

/// <summary>
/// One goal's setup as it runs: the screens of its <see cref="EasyFlowDef"/> in order with Back and Next, the progress strip, the
/// questions' answers, and the Setup checklist that is worked out again from the live facts whenever <see cref="Reevaluate"/> is called
/// (the page does that on every change), so lines tick by themselves. Nothing changes on the PC except through a fix button the
/// user pressed (after its question); leaving halfway, or Back, changes nothing that was not already pressed.
/// </summary>
public sealed partial class EasyFlowViewModel : ObservableObject, IDisposable
{
    private readonly EasyGoal _goal;
    private readonly EasyFlowDef _def;
    private readonly EasyAnswers _answers;
    private readonly EasyFlowServices _services;
    private readonly IReadOnlyList<EasyQuestionViewModel?> _questions;
    private readonly Action _toCards;
    private readonly Action _toPage;
    private EasyFacts _facts = new() { Looking = true };
    private string? _picturesShown;

    public EasyFlowViewModel(EasyGoal goal, EasyFlowDef def, EasyAnswers answers, EasyFlowServices services,
        Func<EasyQuestionKind, Action, EasyQuestionViewModel> makeQuestion, Action toCards, Action toPage)
    {
        _goal = goal;
        _def = def;
        _answers = answers;
        _services = services;
        _toCards = toCards;
        _toPage = toPage;
        _questions = def.Steps.Select(s => s.Kind == EasyStepKind.Question ? makeQuestion(s.Question, Reevaluate) : null).ToList();
        Strip = new[] { new EasyStripItem("Goal") }.Concat(def.Steps.Select(s => new EasyStripItem(s.Title))).ToList();
        BackCommand = new RelayCommand(Back);
        NextCommand = new RelayCommand(Next, () => CanNext);
        Enter(0);
        Reevaluate();
    }

    public IReadOnlyList<EasyStripItem> Strip { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand NextCommand { get; }
    public ObservableCollection<EasyCheckItem> Checks { get; } = new();
    public ObservableCollection<EasyCheckItem> Extras { get; } = new();
    public ObservableCollection<EasyPictureRow> Pictures { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsQuestion), nameof(IsSetup), nameof(IsDone), nameof(Question))] private int _stepIndex;

    private EasyStep Step => _def.Steps[StepIndex];
    public bool IsQuestion => Step.Kind == EasyStepKind.Question;
    public bool IsSetup => Step.Kind == EasyStepKind.Setup;
    public bool IsDone => Step.Kind == EasyStepKind.Done;
    public EasyQuestionViewModel? Question => _questions[StepIndex];

    /// <summary>Whether Next can be pressed: a question is answered enough, or every required line of the checklist is done.</summary>
    [ObservableProperty] private bool _canNext;

    /// <summary>One calm sentence about what the last pressed step did; cleared when the screen changes.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    [ObservableProperty] private string _doneHeadline = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _doneLines = Array.Empty<string>();

    /// <summary>Why Next is dimmed, as the button's tooltip (empty = it can be pressed).</summary>
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
        if (!CanNext || StepIndex >= _def.Steps.Count - 1) return;
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

    /// <summary>Reads the PC again and redraws what depends on it: the choices, the checklist's ticks, the "All set" words, the Next button.</summary>
    public void Reevaluate()
    {
        _facts = _services.Read();
        foreach (var question in _questions) question?.Refresh(_facts);
        if (IsSetup)
        {
            Sync(Checks, Evaluate(_facts));
            SyncPictures();
        }
        if (IsDone)
        {
            Sync(Extras, _def.Extras?.Invoke(_answers, _facts) ?? Array.Empty<EasyCheck>());
            var done = _def.Done(_answers, _facts);
            (DoneHeadline, DoneLines) = (done.Headline, done.Lines);
        }
        (CanNext, NextTip) = Step.Kind switch
        {
            EasyStepKind.Question => Question?.Problem is { Length: > 0 } why ? (false, why) : (true, string.Empty),
            EasyStepKind.Setup => Checks.Where(c => !c.IsOptional).All(c => c.IsDone) && !_facts.Looking ? (true, string.Empty) : (false, "Finish the steps first"),
            _ => (false, string.Empty),
        };
        NextCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<EasyCheck> Evaluate(EasyFacts facts) => facts.Looking
        ? new[] { new EasyCheck("looking", "Looking at your PC", "One moment.", CheckState.Blocked) }
        : _def.Checks(_answers, facts);

    private void Sync(ObservableCollection<EasyCheckItem> items, IReadOnlyList<EasyCheck> checks)
    {
        if (items.Select(i => i.Id).SequenceEqual(checks.Select(c => c.Id)))
        {
            for (var i = 0; i < items.Count; i++) items[i].Update(checks[i]);
            return;
        }
        items.Clear();
        foreach (var check in checks) items.Add(new EasyCheckItem(check, RunFixAsync));
    }

    /// <summary>The small pictures above the checklist: the goal's own, or for "A different sound per app" one per ticked app.</summary>
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

    /// <summary>
    /// A fix button was pressed: the line is looked at again first (it may have been done or blocked meanwhile), its question is asked
    /// when it has one, and only a yes carries the fix out. The outcome is one calm sentence under the list.
    /// </summary>
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
