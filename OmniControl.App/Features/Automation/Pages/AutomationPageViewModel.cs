using System.ComponentModel;
using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation.Pages;

/// The Automation page: the Rules tab (sentence rules: "When ... do ...", and blocks: "While ... don't allow ..."), the From other pages tab (the rules
/// that hold a feature's own rule action, <see cref="AutomationFeatureRules"/>, and the rules that apply a preset,
/// <see cref="AutomationPresets.PresetOf"/>, made in Presets Manager) and the Scripts tab (AutomationPageViewModel.Scripts.cs). Custom actions
/// are made and edited in the Actions and Hotkeys window. The rules and scripts themselves are
/// <see cref="AutomationService"/>'s; this page lists them, adds and removes them, and shows the one status line.
public sealed partial class AutomationPageViewModel : PageViewModel, IDisposable
{
    private const string RulesTab = "Rules", FeatureTab = AutomationFeatureRules.Tab;

    private readonly AutomationService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly AutomationPresets? _presets;
    private readonly AutomationFeatureRules? _features;

    public static readonly PageInfo Info = new(ActionSections.Automation,
        "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M11,7 H13 V11.6 L16.2,13.4 L15.2,15.1 L11,12.7 Z", NavGroups.System,
        "automation schedule scheduler timer task rules when time startup login log in sleep hibernate shut down restart sign out lock power mode wake battery idle script command block powershell cmd batch python");

    public override bool ShowsOwnHeader => true; // the tabs and the pause button sit on the title line

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Tab moves through a rule's name and blocks; Enter on a block changes it; Delete removes it. On a hotkey block, Enter then press the keys (Esc cancels).",
        "Ctrl+Tab switches between the Rules, From other pages and Scripts tabs.",
        $"{ReservedKeys.Emergency} pauses all rules from anywhere; it cannot be changed or blocked.");

    public override PageTabs Tabs { get; } = new(RulesTab, FeatureTab, ScriptsTab);

    // The folding groups of the page (each open unless the user closed it; the tooltip says what the group is).
    public CardGroup AutomationsGroup { get; } = new("Automation/Automations", "Automations", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = AutomationsCard.Tip,
    };

    public CardGroup BlocksGroup { get; } = new("Automation/Blocks", "Blocks", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = BlocksCard.Tip,
    };

    public CardGroup FeatureRulesGroup { get; } = new("Automation/From other pages", "Rules made on other pages", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = FeatureRulesCard.Tip,
    };

    public bool ShowsRules => Tabs.Current == RulesTab;
    public bool ShowsFeatures => Tabs.Current == FeatureTab;

    public ObservableCollection<AutomationItem> Automations { get; } = new();
    public ObservableCollection<AutomationItem> Blocks { get; } = new();
    public ObservableCollection<AutomationItem> FeatureRules { get; } = new();

    public bool NoAutomations => Automations.Count == 0;
    public bool NoBlocks => Blocks.Count == 0;
    public bool NoFeatureRules => FeatureRules.Count == 0;

    /// The one status line: why no rule works right now, or empty.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusLine = string.Empty;
    [ObservableProperty] private string _pauseLabel = "Pause all rules";

    public bool HasStatus => StatusLine.Length > 0;
    public bool CanPause => !_service.SessionOff;
    public bool IsSafeStart => _service.SessionOff;
    public string PauseTip => PauseTipText;

    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    /// <param name="presets">The preset rules' kind and Presets Manager (null: none).</param>
    /// <param name="features">Which page each feature's rule was made on (null: none).</param>
    public AutomationPageViewModel(AutomationService service, Func<DialogMessage, Task<bool>> ask, AutomationPresets? presets, AutomationFeatureRules? features) : base(Info)
    {
        _service = service;
        _ask = ask;
        _presets = presets;
        _features = features;
        foreach (var rule in service.Settings.Rules) Show(rule);
        ShowScripts();
        Automations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoAutomations));
        Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoBlocks));
        FeatureRules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoFeatureRules));
        Tabs.PropertyChanged += OnTabChanged;
        service.Changed += RefreshStatus;
        service.Changed += FollowRules;
        service.Reloaded += ShowAll;
        RefreshStatus();
    }

    private IEnumerable<AutomationItem> RuleRows => Automations.Concat(Blocks).Concat(FeatureRules);

    /// Every rule and script was replaced (an import): the lists are made again from the service's.
    private void ShowAll()
    {
        foreach (var item in RuleRows) item.Dispose();
        Automations.Clear();
        Blocks.Clear();
        FeatureRules.Clear();
        foreach (var rule in _service.Settings.Rules) Show(rule);
        ShowScriptItems();
    }

    public void Dispose()
    {
        Tabs.PropertyChanged -= OnTabChanged;
        _service.Changed -= RefreshStatus;
        _service.Changed -= FollowRules;
        _service.Reloaded -= ShowAll;
        ForgetScripts();
        foreach (var item in RuleRows) item.Dispose();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsRules));
        OnPropertyChanged(nameof(ShowsFeatures));
        OnPropertyChanged(nameof(ShowsScripts));
    }

    private void RefreshStatus()
    {
        StatusLine = _service.SessionOff
            ? AutomationService.SafeStartWords
            : _service.Paused ? AutomationService.PausedWords : string.Empty;
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(IsSafeStart));
        PauseLabel = _service.Paused ? "Resume rules" : "Pause all rules";
    }

    // Read from the rule's actions each time: a preset's apply action or a feature's rule action puts it on From other pages; any other Do rule is on Rules.
    private ObservableCollection<AutomationItem> ListOf(AutomationRule rule) =>
        rule.Kind == AutomationKind.Block ? Blocks
        : AutomationPresets.PresetOf(rule) is not null || _features?.PageOf(rule) is not null ? FeatureRules : Automations;

    private void Show(AutomationRule rule) => ListOf(rule).Add(new AutomationItem(rule, _service, _ask, _presets, ListOf(rule) == FeatureRules ? _features : null));

    /// The service's rules changed (here, in Presets Manager, on a feature's page): rows of rules gone, or no longer of that tab, are dropped,
    /// and a rule gets a row on the tab it belongs to.
    private void FollowRules()
    {
        var rules = _service.Settings.Rules;
        foreach (var list in new[] { Automations, Blocks, FeatureRules })
            foreach (var item in list.Where(i => !rules.Contains(i.Rule) || ListOf(i.Rule) != list).ToList())
            {
                item.Dispose();
                list.Remove(item);
            }
        foreach (var rule in rules.ToList())
            if (ListOf(rule).All(i => i.Rule != rule)) Show(rule);
    }

    [RelayCommand]
    private void AddAutomation() => _service.Add(AutomationKind.Do);

    [RelayCommand]
    private void AddBlock() => _service.Add(AutomationKind.Block);

    [RelayCommand]
    private void TurnRulesOn() => _service.TurnRulesOn();

    [RelayCommand]
    private void TogglePause() => _service.TogglePause();
}
