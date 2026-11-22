using System.ComponentModel;
using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation.Pages;

/// The Automation page: the Rules tab (sentence rules: "When ... do ...", and blocks: "While ... don't allow ..."), the Presets tab (the rules
/// made from the Presets window, by their <see cref="AutomationRule.Preset"/>), the Custom actions tab (scripts, programs, files and addresses
/// that become actions of their own) and the Scripts tab (AutomationPageViewModel.Scripts.cs). The rules and actions themselves are
/// <see cref="AutomationService"/>'s; this page lists them, adds and removes them, and shows the one status line.
public sealed partial class AutomationPageViewModel : PageViewModel, IDisposable
{
    private const string RulesTab = "Rules", PresetsTab = "Presets", CustomTab = "Custom actions";

    private readonly AutomationService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly AutomationPresets? _presets;

    public static readonly PageInfo Info = new(ActionSections.Automation,
        "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M11,7 H13 V11.6 L16.2,13.4 L15.2,15.1 L11,12.7 Z", NavGroups.System,
        "automation schedule scheduler timer task rules when time startup login log in sleep hibernate shut down restart sign out lock power mode wake battery idle script command custom action block powershell cmd batch python");

    public override bool ShowsOwnHeader => true; // the tabs and the pause button sit on the title line

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Tab moves through a rule's name and blocks; Enter on a block changes it; Delete removes it. On a hotkey block, Enter then press the keys (Esc cancels).",
        "Ctrl+Tab switches between the Rules, Presets, Custom actions and Scripts tabs.",
        $"{ReservedKeys.Emergency} pauses all rules from anywhere; it cannot be changed or blocked.");

    public override PageTabs Tabs { get; } = new(RulesTab, PresetsTab, CustomTab, ScriptsTab);

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

    public CardGroup PresetRulesGroup { get; } = new("Automation/Preset rules", "Preset rules", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = PresetRulesCard.Tip,
    };

    public CardGroup CustomsGroup { get; } = new("Automation/Custom actions", "Custom actions", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = CustomsCard.Tip,
    };
    public bool ShowsRules => Tabs.Current == RulesTab;
    public bool ShowsPresets => Tabs.Current == PresetsTab;
    public bool ShowsCustom => Tabs.Current == CustomTab;

    public ObservableCollection<AutomationItem> Automations { get; } = new();
    public ObservableCollection<AutomationItem> Blocks { get; } = new();
    public ObservableCollection<AutomationItem> PresetRules { get; } = new();
    public ObservableCollection<CustomActionItem> Customs { get; } = new();

    public bool NoAutomations => Automations.Count == 0;
    public bool NoBlocks => Blocks.Count == 0;
    public bool NoPresetRules => PresetRules.Count == 0;
    public bool NoCustoms => Customs.Count == 0;

    /// The one status line: why no rule works right now, or empty.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusLine = string.Empty;
    [ObservableProperty] private string _pauseLabel = "Pause all rules";

    public bool HasStatus => StatusLine.Length > 0;
    public bool CanPause => !_service.SessionOff;
    public bool IsSafeStart => _service.SessionOff;
    public string PauseTip => PauseTipText;

    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    /// <param name="presets">The preset rules' kind and the Presets window (null: none).</param>
    public AutomationPageViewModel(AutomationService service, Func<DialogMessage, Task<bool>> ask, AutomationPresets? presets) : base(Info)
    {
        _service = service;
        _ask = ask;
        _presets = presets;
        foreach (var rule in service.Settings.Rules) Show(rule);
        foreach (var custom in service.Settings.Customs) Customs.Add(new CustomActionItem(custom, service, ask));
        service.CustomAdded += OnCustomAdded;
        service.CustomRemoved += OnCustomRemoved;
        ShowScripts();
        Automations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoAutomations));
        Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoBlocks));
        PresetRules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoPresetRules));
        Customs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoCustoms));
        Tabs.PropertyChanged += OnTabChanged;
        service.Changed += RefreshStatus;
        service.Changed += FollowRules;
        service.Reloaded += ShowAll;
        RefreshStatus();
    }

    private IEnumerable<AutomationItem> RuleRows => Automations.Concat(Blocks).Concat(PresetRules);

    /// Every rule, custom action and script was replaced (an import): the lists are made again from the service's.
    private void ShowAll()
    {
        foreach (var item in RuleRows) item.Dispose();
        foreach (var item in Customs) item.Dispose();
        Automations.Clear();
        Blocks.Clear();
        PresetRules.Clear();
        Customs.Clear();
        foreach (var rule in _service.Settings.Rules) Show(rule);
        foreach (var custom in _service.Settings.Customs) Customs.Add(new CustomActionItem(custom, _service, _ask));
        ShowScriptItems();
    }

    public void Dispose()
    {
        Tabs.PropertyChanged -= OnTabChanged;
        _service.Changed -= RefreshStatus;
        _service.Changed -= FollowRules;
        _service.Reloaded -= ShowAll;
        _service.CustomAdded -= OnCustomAdded;
        _service.CustomRemoved -= OnCustomRemoved;
        ForgetScripts();
        foreach (var item in RuleRows) item.Dispose();
        foreach (var item in Customs) item.Dispose();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsRules));
        OnPropertyChanged(nameof(ShowsPresets));
        OnPropertyChanged(nameof(ShowsCustom));
        OnPropertyChanged(nameof(ShowsScripts));
    }

    private void RefreshStatus()
    {
        StatusLine = _service.SessionOff
            ? AutomationService.SafeStartWords
            : _service.Paused ? "All rules are paused. Nothing is blocked either." : string.Empty;
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(IsSafeStart));
        PauseLabel = _service.Paused ? "Resume rules" : "Pause all rules";
    }

    // A preset rule is a Do rule with its source set; blocks and other Do rules stay on the Rules tab.
    private ObservableCollection<AutomationItem> ListOf(AutomationRule rule) =>
        rule.Kind == AutomationKind.Block ? Blocks : rule.Preset.Length > 0 ? PresetRules : Automations;

    private void Show(AutomationRule rule) => ListOf(rule).Add(new AutomationItem(rule, _service, _ask, _presets));

    /// The service's rules changed (here, in the Presets window, an Undo there): rows of rules gone are dropped, new rules get a row.
    private void FollowRules()
    {
        var rules = _service.Settings.Rules;
        foreach (var list in new[] { Automations, Blocks, PresetRules })
            foreach (var item in list.Where(i => !rules.Contains(i.Rule)).ToList())
            {
                item.Dispose();
                list.Remove(item);
            }
        foreach (var rule in rules.Where(r => RuleRows.All(i => i.Rule != r)).ToList()) Show(rule);
    }

    // A custom action made or removed anywhere (this tab, or the Choose-the-action window) shows here at once.
    private void OnCustomAdded(CustomAction custom) => Customs.Add(new CustomActionItem(custom, _service, _ask));

    private void OnCustomRemoved(CustomAction custom)
    {
        if (Customs.FirstOrDefault(c => c.Custom == custom) is not { } item) return;
        item.Dispose();
        Customs.Remove(item);
    }

    [RelayCommand]
    private void AddAutomation() => _service.Add(AutomationKind.Do);

    [RelayCommand]
    private void AddBlock() => _service.Add(AutomationKind.Block);

    [RelayCommand]
    private void OpenPresets() => _presets?.Open(null);

    [RelayCommand]
    private void AddCustom()
    {
        _service.AddCustom();
        Tabs.Select(CustomTab);
    }

    [RelayCommand]
    private void TurnRulesOn() => _service.TurnRulesOn();

    [RelayCommand]
    private void TogglePause() => _service.TogglePause();
}
