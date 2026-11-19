using System.Collections.Specialized;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// The three things the Easy view can show: the goal cards, one goal's guided setup, the Easy page.
public enum EasyScreen { Welcome, Flow, Page }

/// The Easy view of the Volume page: the welcome (five goal cards to tick), a goal's guided setup (<see cref="EasyFlowViewModel"/>, driven by
/// <see cref="EasyFlows"/>), and the Easy page (the "Everything" card, the apps and microphones, the footer). It owns no sound state: its rows wrap the Advanced page's own
/// <see cref="VolumeRow"/>s and its drawers edit the same effect chains through <see cref="MixerService"/>, so Easy and Advanced always agree.
/// Which programs are listed is the Mixer's own list (open ones first, "Show closed" honoured), read from the Outputs group.
public sealed partial class EasyViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly EffectsSupportService _support;
    private readonly EffectsEngine _engine;
    private readonly VolumeGroup _outputs;
    private readonly VolumeGroup _inputs;
    private readonly Action<string?> _openFx;
    private readonly ISettingSlot<bool> _welcomeDone;
    private readonly EasyFlowServices _flowServices;
    private readonly Dictionary<string, EasyRow> _cache = new();
    private readonly IAudioDefaultsMixer _defaults;
    private readonly IDefaultsSnapshot _snapshot = new FileDefaultsSnapshot();
    private readonly Dictionary<string, EasyEngine> _engines = new(); // the way the user picked per goal, for the next "Set up again"
    private readonly DispatcherTimer _fade = new() { Interval = Timing.EasyConfirmation };
    private bool _rowsPending;

    /// <param name="flow">What a guided setup needs: the live facts (the page's apps are added here), carrying out a fix, asking a question.</param>
    public EasyViewModel(MixerService mixer, EffectsSupportService support, EffectsEngine engine, MixerItem master, VolumeGroup outputs, VolumeGroup inputs,
        Action<string?> openFx, EasyFlowServices flow, ISettingSlot<bool> welcomeDone)
    {
        _welcomeDone = welcomeDone;
        _mixer = mixer;
        _defaults = new MixerDefaultsStore(mixer);
        _support = support;
        _engine = engine;
        _outputs = outputs;
        _inputs = inputs;
        _openFx = openFx;
        _flowServices = flow with { Read = () => flow.Read() with { Programs = ProgramOptions() } };
        Master = new EasyRow(master, mixer, engine, openFx, flow.Confirm);
        _screen = welcomeDone.Value ? EasyScreen.Page : EasyScreen.Welcome;
        SyncRows();
        Refresh();
        BuildGoals();
        outputs.Items.CollectionChanged += OnListChanged;
        inputs.Items.CollectionChanged += OnListChanged;
        mixer.Updated += Refresh;
        mixer.FxChanged += OnFxChanged;
        support.Changed += Refresh;
        engine.StatusChanged += OnStatusChanged;
        engine.PlanChanged += Refresh;
        _fade.Tick += OnFade;
    }

    /// The "Everything" card: the master's level and mute, and its Sound drawer.
    public EasyRow Master { get; }

    /// The two folding cards of the page (open unless the user closed them).
    public CardGroup EverythingGroup { get; } = new("Easy/Everything", "Everything", "all your apps and speakers") { ExpandedByDefault = true };

    public CardGroup AppsGroup { get; } = new("Easy/Apps and microphones", "Apps and microphones", string.Empty) { ExpandedByDefault = true };

    /// The apps (the Mixer's listed programs) and then the microphones.
    public ObservableCollection<EasyRow> Apps { get; } = new();

    public bool HasApps => Apps.Count > 0;

    /// The searchable "Add program" pop-down of the Mixer, reused; null while the page has none.
    public AddProgramMenuViewModel? AddProgram => _outputs.ProgramTools?.Add;

    // ---- which screen ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsFlow), nameof(IsPage))] private EasyScreen _screen;

    public bool IsWelcome => Screen == EasyScreen.Welcome;
    public bool IsFlow => Screen == EasyScreen.Flow;
    public bool IsPage => Screen == EasyScreen.Page;

    /// The guided setup that is open (<see cref="EasyScreen.Flow"/>); null otherwise.
    [ObservableProperty] private EasyFlowViewModel? _flow;

    // ---- the welcome ----

    public ObservableCollection<GoalCard> Goals { get; } = new();

    private readonly EasySelection _selection = new();

    /// Whether at least one goal is ticked (Next is off until then).
    public bool HasSelection => _selection.CanStart;

    /// The "What you will need" box: the lines for the ticked goals, redrawn whenever the ticks or the PC change.
    public ObservableCollection<EasyCheckItem> Needs { get; } = new();

    /// Makes the cards from the master list of goals, none ticked, each with whether it is on now (read from the same facts the setups use).
    private void BuildGoals()
    {
        var facts = _flowServices.Read();
        _selection.Clear();
        Goals.Clear();
        foreach (var goal in EasyGoals.All) Goals.Add(new GoalCard(goal, EasyGoals.IsOn(goal.Id, facts), Toggle));
        SelectionChanged(facts);
    }

    /// A card was clicked: it is ticked or unticked, and the box under the cards follows.
    private void Toggle(GoalCard card)
    {
        card.IsSelected = _selection.Toggle(card.Goal.Id);
        SelectionChanged();
    }

    private void SelectionChanged(EasyFacts? facts = null)
    {
        OnPropertyChanged(nameof(HasSelection));
        StartCommand.NotifyCanExecuteChanged();
        RefreshNeeds(facts ?? _flowServices.Read());
    }

    private void RefreshNeeds(EasyFacts facts) => EasyCheckItem.Sync(Needs, EasyNeeds.Of(_selection.Ids, facts), GetNeedAsync);

    /// A "Get" button of the box: the line is looked at again first, then the same installer step the checklists use runs (only when pressed).
    private async Task GetNeedAsync(string id)
    {
        var facts = _flowServices.Read();
        if (EasyNeeds.Of(_selection.Ids, facts).FirstOrDefault(l => l.Id == id) is not { Fix: { } fix }) return;
        foreach (var line in Needs) line.IsBusy = true;
        try
        {
            await _flowServices.Actions.RunAsync(fix, new EasyAnswers(), facts);
        }
        finally
        {
            foreach (var line in Needs) line.IsBusy = false;
        }
        RefreshNeeds(_flowServices.Read());
    }

    /// "Next": the guided setups of the ticked goals start, one after another in the order of the cards.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Start()
    {
        WelcomeAnswered();
        BeginSetup(_selection.Goals);
    }

    [RelayCommand]
    private void Skip()
    {
        WelcomeAnswered();
        Screen = EasyScreen.Page;
    }

    /// The welcome does not come back by itself after a setup or Skip (only "Set up again" brings it).
    private void WelcomeAnswered()
    {
        if (!_welcomeDone.Value) _welcomeDone.Value = true;
    }

    [RelayCommand]
    private void SetUpAgain() => ShowCards();

    // ---- the guided setups ----

    private EasySequence? _sequence;

    private void BeginSetup(IReadOnlyList<EasyGoal> goals)
    {
        CloseFlow();
        _sequence = new EasySequence(goals, MakeFlow, flow => Flow = flow, FinishSetup);
        Screen = EasyScreen.Flow;
        _sequence.Start();
    }

    /// One goal's setup; the engine the user chose for it last time is the pre-selection unless an earlier goal of this run settled one.
    private EasyFlowViewModel MakeFlow(EasyGoal goal, EasyAnswers answers, EasyFlowPlace place)
    {
        answers.Engine ??= _engines.TryGetValue(goal.Id, out var engine) ? engine : null;
        return new EasyFlowViewModel(goal, EasyFlows.Of(goal.Id)!, answers, _flowServices,
            (kind, changed) => new EasyQuestionViewModel(kind, answers, changed, kind == EasyQuestionKind.Sound ? NewMasterSound() : null, AddProgram),
            ShowCards, place.Finish, chosen => _engines[goal.Id] = chosen, place);
    }

    /// The master's Sound drawer for the setup's Sound question: never greyed (the checklist says what is still missing), and its own so it is not the page's.
    private EasySound NewMasterSound() => new(_mixer, _engine, null, () => _openFx(null), () => null, _flowServices.Confirm);

    /// "Back" from the first screen, "Set up something else" and "Set up again": the cards, each marked "on" where the app can tell.
    private void ShowCards()
    {
        CloseFlow();
        BuildGoals();
        Screen = EasyScreen.Welcome;
    }

    /// The end of a run: the Easy page with one line about what was set up; each ticked app's Sound drawer is open once.
    private void FinishSetup(EasySummary summary)
    {
        CloseFlow();
        Screen = EasyScreen.Page;
        foreach (var result in summary.Completed.Where(r => r.Goal.Id == EasyGoals.PerApp))
            foreach (var row in Apps.Where(r => r.Row is MixerItem && result.Answers.Programs.Contains(r.Row.Key))) row.Sound.IsOpen = true;
        Confirm(summary is { Completed.Count: 1, Skipped.Count: 0 } ? OneGoalLine(summary.Completed[0].Goal) : summary.Text);
    }

    private string OneGoalLine(EasyGoal goal) => goal.Id == EasyGoals.Better
        ? Master.Sound.IsActive ? $"Your sound is on: {Master.Sound.Name}." : "Your sound is Normal: nothing is changed."
        : $"{goal.Title}: set up.";

    private void CloseFlow()
    {
        _sequence?.Dispose();
        _sequence = null;
        Flow = null;
    }

    // ---- the page ----

    /// A short line under the title after a setup; it fades after <see cref="Timing.EasyConfirmation"/>.
    [ObservableProperty] private string _confirmation = string.Empty;

    [ObservableProperty] private double _confirmationOpacity;

    private void Confirm(string text, TimeSpan? hold = null)
    {
        CanUndoRestore = false;
        Confirmation = text;
        ConfirmationOpacity = 1;
        _fade.Stop();
        _fade.Interval = hold ?? Timing.EasyConfirmation;
        _fade.Start();
    }

    private void OnFade(object? sender, EventArgs e)
    {
        _fade.Stop();
        ConfirmationOpacity = 0;
        CanUndoRestore = false;
    }

    /// What is set up, in a few words ("Bass boost sound on every app"); empty when nothing is.
    [ObservableProperty] private string _setUpText = string.Empty;

    [ObservableProperty] private bool _isSetUp;

    /// What "Something not working? Fix it" found or did, as one calm sentence.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFixNote))] private string _fixNote = string.Empty;

    public bool HasFixNote => FixNote.Length > 0;

    /// Looks at every row the page shows: the first one that needs a setup step has that step started (the same step the Advanced row offers);
    /// otherwise the first that cannot apply its sound says why; otherwise nothing wrong is found, and the line says so.
    [RelayCommand]
    private void FixIt()
    {
        var rows = Apps.Prepend(Master).ToList();
        var needing = rows.FirstOrDefault(r => r.Row.HasFix);
        if (needing?.Row.Fix is { } fix)
        {
            FixNote = $"{needing.Row.Name}: {Sentence(needing.Row.Status, "it needs a setup step")}. Starting: {fix.Label}.";
            fix.Command.Execute(null);
            return;
        }
        var blocked = rows.FirstOrDefault(r => r.Sound.Availability.Reason is not null);
        FixNote = blocked is null
            ? "Nothing wrong found. Advanced shows each row's status."
            : $"{blocked.Row.Name}: {Sentence(blocked.Sound.Availability.Reason!, "its sound can't be applied")}.";
    }

    private static string Sentence(string text, string fallback) => (text.Length > 0 ? text : fallback).TrimEnd('.');

    // ---- rows and what they say ----

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rowsPending) return;
        _rowsPending = true;
        // The Mixer clears and refills its lists: wait for it to finish, then read them once.
        Dispatcher.UIThread.Post(() => { _rowsPending = false; SyncRows(); Refresh(); });
    }

    private void OnFxChanged(string? key) => Refresh();

    private void OnStatusChanged(string? key) => Refresh();

    private EasyRow RowFor(VolumeRow row)
    {
        if (!_cache.TryGetValue(row.Key, out var easy)) _cache[row.Key] = easy = new EasyRow(row, _mixer, _engine, _openFx, _flowServices.Confirm);
        return easy;
    }

    /// The Mixer's programs as things a question can tick (the playing ones marked).
    private IReadOnlyList<EasyOption> ProgramOptions() =>
        Apps.Where(r => r.Row is MixerItem).Select(r => new EasyOption(r.Row.Key, r.Row.Name, r.State == "playing" ? "playing" : string.Empty)).ToList();

    /// Puts the apps in the Mixer's order: its listed programs, then the microphones; rows that left the lists go.
    private void SyncRows()
    {
        var wanted = _outputs.Items.OfType<MixerItem>().Where(i => !i.IsMaster).Cast<VolumeRow>()
            .Concat(_inputs.Items.OfType<InputItem>()).Select(RowFor).ToList();
        foreach (var gone in _cache.Where(c => !wanted.Contains(c.Value)).ToList())
        {
            gone.Value.Dispose();
            _cache.Remove(gone.Key);
        }
        if (wanted.SequenceEqual(Apps)) return;
        Apps.Clear();
        foreach (var row in wanted) Apps.Add(row);
        OnPropertyChanged(nameof(HasApps));
    }

    /// Reads everything the page shows again: every drawer, every row's state word, the footer.
    public void Refresh()
    {
        Master.Sound.Refresh();
        foreach (var row in Apps)
        {
            row.Update(_mixer.Running.FirstOrDefault(r => r.Key == row.Row.Key)?.IsPlaying ?? false);
            row.Sound.Refresh();
        }
        var parts = new List<string>();
        if (Master.Sound.IsActive) parts.Add($"{Master.Sound.Name} sound on every app");
        var own = Apps.Count(r => r.Row is MixerItem && r.Sound.IsActive);
        if (own > 0) parts.Add(own == 1 ? "1 app with its own sound" : $"{own} apps with their own sound");
        if (Apps.Any(r => r.Row is InputItem && r.Sound.IsActive)) parts.Add("microphone cleanup");
        SetUpText = string.Join(" · ", parts);
        IsSetUp = parts.Count > 0;
        RefreshRestore();
        Flow?.Reevaluate();
        if (IsWelcome) RefreshNeeds(_flowServices.Read());
    }

    /// The Easy view came on screen: read the rows again, they may have changed in Advanced.
    public void Shown()
    {
        SyncRows();
        Refresh();
    }

    /// The hotkey: the master's next sound preset (the same chips as the drawer).
    public async Task NextMasterPresetAsync()
    {
        if (await Master.Sound.NextPresetAsync()) Confirm($"Everything: {Master.Sound.Name}.");
    }

    public void Dispose()
    {
        _outputs.Items.CollectionChanged -= OnListChanged;
        _inputs.Items.CollectionChanged -= OnListChanged;
        _mixer.Updated -= Refresh;
        _mixer.FxChanged -= OnFxChanged;
        _support.Changed -= Refresh;
        _engine.StatusChanged -= OnStatusChanged;
        _engine.PlanChanged -= Refresh;
        _fade.Stop();
        _fade.Tick -= OnFade;
        CloseFlow();
        foreach (var row in _cache.Values) row.Dispose();
        Master.Dispose();
    }
}
