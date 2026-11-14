using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages.Volume.Easy.Flow;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>The three things the Easy view can show: the goal cards, one goal's guided setup, the Easy page.</summary>
public enum EasyScreen { Welcome, Flow, Page }

/// <summary>
/// The Easy view of the Volume page: the welcome (six goal cards), a goal's guided setup (<see cref="EasyFlowViewModel"/>, driven by
/// <see cref="EasyFlows"/>), and the Easy page (the "Everything" card, the apps and microphones, the footer). It owns no sound state: its rows wrap the Advanced page's own
/// <see cref="VolumeRow"/>s and its drawers edit the same effect chains through <see cref="MixerService"/>, so Easy and Advanced always agree.
/// Which programs are listed is the Mixer's own list (open ones first, "Show closed" honoured), read from the Outputs group.
/// </summary>
public sealed partial class EasyViewModel : ObservableObject, IDisposable
{
    private readonly MixerService _mixer;
    private readonly SoundModeService _modes;
    private readonly EffectsEngine _engine;
    private readonly VolumeGroup _outputs;
    private readonly VolumeGroup _inputs;
    private readonly Action<string?> _openFx;
    private readonly EasyFlowServices _flowServices;
    private readonly Dictionary<string, EasyRow> _cache = new();
    private readonly DispatcherTimer _fade = new() { Interval = Timing.EasyConfirmation };
    private bool _rowsPending;

    /// <param name="flow">What a guided setup needs: the live facts (the page's apps are added here), carrying out a fix, asking a question.</param>
    public EasyViewModel(MixerService mixer, SoundModeService modes, EffectsEngine engine, MixerItem master, VolumeGroup outputs, VolumeGroup inputs,
        Action<string?> openFx, EasyFlowServices flow)
    {
        _mixer = mixer;
        _modes = modes;
        _engine = engine;
        _outputs = outputs;
        _inputs = inputs;
        _openFx = openFx;
        _flowServices = flow with { Read = () => flow.Read() with { Programs = ProgramOptions() } };
        Master = new EasyRow(master, mixer, engine, openFx);
        _screen = AppSettings.Current.EasyWelcomeDone ? EasyScreen.Page : EasyScreen.Welcome;
        SyncRows();
        Refresh();
        BuildGoals();
        outputs.Items.CollectionChanged += OnListChanged;
        inputs.Items.CollectionChanged += OnListChanged;
        mixer.Updated += Refresh;
        mixer.FxChanged += OnFxChanged;
        modes.Changed += Refresh;
        engine.StatusChanged += OnStatusChanged;
        engine.PlanChanged += Refresh;
        _fade.Tick += OnFade;
    }

    /// <summary>The "Everything" card: the master's level and mute, and its Sound drawer.</summary>
    public EasyRow Master { get; }

    /// <summary>The apps (the Mixer's listed programs) and then the microphones.</summary>
    public ObservableCollection<EasyRow> Apps { get; } = new();

    public bool HasApps => Apps.Count > 0;

    /// <summary>The searchable "Add program" pop-down of the Mixer, reused; null while the page has none.</summary>
    public AddProgramMenuViewModel? AddProgram => _outputs.ProgramTools?.Add;

    // ---- which screen ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsFlow), nameof(IsPage))] private EasyScreen _screen;

    public bool IsWelcome => Screen == EasyScreen.Welcome;
    public bool IsFlow => Screen == EasyScreen.Flow;
    public bool IsPage => Screen == EasyScreen.Page;

    /// <summary>The guided setup that is open (<see cref="EasyScreen.Flow"/>); null otherwise.</summary>
    [ObservableProperty] private EasyFlowViewModel? _flow;

    // ---- the welcome ----

    public ObservableCollection<GoalCard> Goals { get; } = new();

    /// <summary>Makes the cards from the master list of goals, each with whether it is on now (read from the same facts the setups use).</summary>
    private void BuildGoals()
    {
        var facts = _flowServices.Read();
        Goals.Clear();
        foreach (var goal in EasyGoals.All) Goals.Add(new GoalCard(goal, EasyGoals.IsOn(goal.Id, facts), Choose));
    }

    /// <summary>A goal was chosen: its guided setup opens; a goal with none ("Just the volume") goes straight to the page.</summary>
    private void Choose(EasyGoal goal)
    {
        WelcomeAnswered();
        if (EasyFlows.Of(goal.Id) is { } def) StartFlow(goal, def);
        else Screen = EasyScreen.Page;
    }

    [RelayCommand]
    private void Skip()
    {
        WelcomeAnswered();
        Screen = EasyScreen.Page;
    }

    /// <summary>The welcome does not come back by itself after a goal or Skip (only "Set up again" brings it).</summary>
    private static void WelcomeAnswered()
    {
        if (AppSettings.Current.EasyWelcomeDone) return;
        AppSettings.Current.EasyWelcomeDone = true;
        AppSettings.Current.Save();
    }

    [RelayCommand]
    private void SetUpAgain() => ShowCards();

    // ---- a goal's guided setup ----

    private void StartFlow(EasyGoal goal, EasyFlowDef def)
    {
        CloseFlow();
        var answers = new EasyAnswers();
        Flow = new EasyFlowViewModel(goal, def, answers, _flowServices,
            (kind, changed) => new EasyQuestionViewModel(kind, answers, changed, kind == EasyQuestionKind.Sound ? NewMasterSound() : null, AddProgram),
            ShowCards, () => FinishFlow(goal, answers));
        Screen = EasyScreen.Flow;
    }

    /// <summary>The master's Sound drawer for the setup's Sound question: never greyed (the checklist says what is still missing), and its own so it is not the page's.</summary>
    private EasySound NewMasterSound() => new(_mixer, _engine, null, () => _openFx(null), () => null);

    /// <summary>"Back" from the first screen, "Set up something else" and "Set up again": the six cards, each marked "on" where the app can tell.</summary>
    private void ShowCards()
    {
        CloseFlow();
        BuildGoals();
        Screen = EasyScreen.Welcome;
    }

    /// <summary>"Go to Volume": the Easy page with one line about what was set up; each ticked app's Sound drawer is open once.</summary>
    private void FinishFlow(EasyGoal goal, EasyAnswers answers)
    {
        CloseFlow();
        Screen = EasyScreen.Page;
        if (goal.Id == EasyGoals.PerApp)
            foreach (var row in Apps.Where(r => r.Row is MixerItem && answers.Programs.Contains(r.Row.Key))) row.Sound.IsOpen = true;
        Confirm(goal.Id == EasyGoals.Better
            ? Master.Sound.IsActive ? $"Your sound is on: {Master.Sound.Name}." : "Your sound is Normal: nothing is changed."
            : $"{goal.Title}: set up.");
    }

    private void CloseFlow()
    {
        Flow?.Dispose();
        Flow = null;
    }

    // ---- the page ----

    /// <summary>A short line under the title after a setup; it fades after <see cref="Timing.EasyConfirmation"/>.</summary>
    [ObservableProperty] private string _confirmation = string.Empty;

    [ObservableProperty] private double _confirmationOpacity;

    private void Confirm(string text)
    {
        Confirmation = text;
        ConfirmationOpacity = 1;
        _fade.Stop();
        _fade.Start();
    }

    private void OnFade(object? sender, EventArgs e)
    {
        _fade.Stop();
        ConfirmationOpacity = 0;
    }

    /// <summary>What is set up, in a few words ("Bass boost sound on every app"); empty when nothing is.</summary>
    [ObservableProperty] private string _setUpText = string.Empty;

    [ObservableProperty] private bool _isSetUp;

    /// <summary>What "Something not working? Fix it" found or did, as one calm sentence.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFixNote))] private string _fixNote = string.Empty;

    public bool HasFixNote => FixNote.Length > 0;

    /// <summary>
    /// Looks at every row the page shows: the first one that needs a setup step has that step started (the same step the Advanced row offers);
    /// otherwise the first that cannot apply its sound says why; otherwise nothing wrong is found, and the line says so.
    /// </summary>
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
        if (!_cache.TryGetValue(row.Key, out var easy)) _cache[row.Key] = easy = new EasyRow(row, _mixer, _engine, _openFx);
        return easy;
    }

    /// <summary>The Mixer's programs as things a question can tick (the playing ones marked).</summary>
    private IReadOnlyList<EasyOption> ProgramOptions() =>
        Apps.Where(r => r.Row is MixerItem).Select(r => new EasyOption(r.Row.Key, r.Row.Name, r.State == "playing" ? "playing" : string.Empty)).ToList();

    /// <summary>Puts the apps in the Mixer's order: its listed programs, then the microphones; rows that left the lists go.</summary>
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

    /// <summary>Reads everything the page shows again: every drawer, every row's state word, the footer.</summary>
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
        Flow?.Reevaluate();
    }

    /// <summary>The Easy view came on screen: read the rows again, they may have changed in Advanced.</summary>
    public void Shown()
    {
        SyncRows();
        Refresh();
    }

    /// <summary>The hotkey: the master's next sound preset (the same chips as the drawer).</summary>
    public void NextMasterPreset()
    {
        Master.Sound.NextPreset();
        Confirm($"Everything: {Master.Sound.Name}.");
    }

    public void Dispose()
    {
        _outputs.Items.CollectionChanged -= OnListChanged;
        _inputs.Items.CollectionChanged -= OnListChanged;
        _mixer.Updated -= Refresh;
        _mixer.FxChanged -= OnFxChanged;
        _modes.Changed -= Refresh;
        _engine.StatusChanged -= OnStatusChanged;
        _engine.PlanChanged -= Refresh;
        _fade.Stop();
        _fade.Tick -= OnFade;
        CloseFlow();
        foreach (var row in _cache.Values) row.Dispose();
        Master.Dispose();
    }
}
