using System.Collections.ObjectModel;
using OmniControl.App.Dialogs;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Automation;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Automation;

/// <summary>
/// The Automation page: the Rules tab (sentence rules: "When ... do ...", and blocks: "While ... don't allow ..."), the Custom
/// actions tab (scripts, programs, files and addresses that become actions of their own) and the Scripts tab (AutomationPageViewModel.Scripts.cs). The rules and actions themselves are
/// <see cref="AutomationService"/>'s; this page lists them, adds and removes them, and shows the one status line.
/// </summary>
public sealed partial class AutomationPageViewModel : PageViewModel, IDisposable
{
    private const string RulesTab = "Rules", CustomTab = "Custom actions";

    private readonly AutomationService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    public override string Title => AutomationService.Category;

    public override string? NavGroup => NavGroups.Workflow;
    public override string Icon => "M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M11,7 H13 V11.6 L16.2,13.4 L15.2,15.1 L11,12.7 Z";
    public override bool ShowsOwnHeader => true; // the tabs and the pause button sit on the title line
    public override string Keywords =>
        "automation schedule scheduler timer task rules when time startup login log in sleep hibernate shut down restart sign out lock power mode wake battery idle script command custom action block powershell cmd batch python";

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Tab moves through a rule's name and blocks; Enter on a block changes it; Delete removes it. On a hotkey block, Enter then press the keys (Esc cancels).",
        "Ctrl+Tab switches between the Rules, Custom actions and Scripts tabs.",
        $"{ReservedKeys.Emergency} pauses all rules from anywhere; it cannot be changed or blocked.");

    public PageTabs Tabs { get; } = new(RulesTab, CustomTab, ScriptsTab);

    // The three folding groups of the page (each open unless the user closed it; the tooltip says what the group is).
    public CardGroup AutomationsGroup { get; } = new("Automation/Automations", "Automations", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = "When something happens (and the conditions hold), do one or more actions. Each also becomes an action called Run: its name.",
    };

    public CardGroup BlocksGroup { get; } = new("Automation/Blocks", "Blocks", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = "While the conditions hold, the chosen actions are not allowed, whether a hotkey, a mouse button, a gesture or a rule starts them. Pausing all rules lifts every block.",
    };

    public CardGroup CustomsGroup { get; } = new("Automation/Custom actions", "Custom actions", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = "Your own actions: a command, a script, a program, a file or folder, a web address. They run as you (never as administrator), and appear in Shortcuts, the mouse and gesture pickers and every rule.",
    };
    public bool ShowsRules => Tabs.Current == RulesTab;
    public bool ShowsCustom => Tabs.Current == CustomTab;

    public ObservableCollection<AutomationItem> Automations { get; } = new();
    public ObservableCollection<AutomationItem> Blocks { get; } = new();
    public ObservableCollection<CustomActionItem> Customs { get; } = new();

    public bool NoAutomations => Automations.Count == 0;
    public bool NoBlocks => Blocks.Count == 0;
    public bool NoCustoms => Customs.Count == 0;

    /// <summary>The one status line: why no rule works right now, or empty.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusLine = string.Empty;
    [ObservableProperty] private string _pauseLabel = "Pause all rules";

    public bool HasStatus => StatusLine.Length > 0;
    public bool CanPause => !_service.SessionOff;
    public bool IsSafeStart => _service.SessionOff;
    public string PauseTip => $"Stops every rule and block at once. {ReservedKeys.Emergency} does the same from anywhere.";

    public override IEnumerable<SearchEntry> SearchEntries =>
        Automations.Concat(Blocks).Select(r => Entry(r, r.Label, $"automation rule schedule timer task {r.Line}", RulesTab))
            .Concat(Customs.Select(c => Entry(c, c.Label, $"custom action script command program url {c.Custom.Target}", CustomTab)))
            .Concat(ScriptSearchEntries);

    /// <param name="ask">Shows a short message with a confirm button; true when confirmed.</param>
    public AutomationPageViewModel(AutomationService service, Func<DialogMessage, Task<bool>> ask)
    {
        _service = service;
        _ask = ask;
        foreach (var rule in service.Settings.Rules) Show(rule);
        foreach (var custom in service.Settings.Customs) Customs.Add(new CustomActionItem(custom, service, ask));
        service.CustomAdded += OnCustomAdded;
        service.CustomRemoved += OnCustomRemoved;
        ShowScripts();
        Automations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoAutomations));
        Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoBlocks));
        Customs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoCustoms));
        Tabs.PropertyChanged += OnTabChanged;
        service.Changed += RefreshStatus;
        RefreshStatus();
    }

    public void Dispose()
    {
        Tabs.PropertyChanged -= OnTabChanged;
        _service.Changed -= RefreshStatus;
        _service.CustomAdded -= OnCustomAdded;
        _service.CustomRemoved -= OnCustomRemoved;
        ForgetScripts();
        foreach (var item in Automations.Concat(Blocks)) item.Dispose();
        foreach (var item in Customs) item.Dispose();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageTabs.Current)) return;
        OnPropertyChanged(nameof(ShowsRules));
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

    private AutomationItem Show(AutomationRule rule)
    {
        var item = new AutomationItem(rule, _service, _ask, Remove);
        (rule.Kind == AutomationKind.Block ? Blocks : Automations).Add(item);
        return item;
    }

    private void Remove(AutomationItem item)
    {
        item.Dispose();
        (item.IsBlock ? Blocks : Automations).Remove(item);
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
    private void AddAutomation() => Show(_service.Add(AutomationKind.Do));

    [RelayCommand]
    private void AddBlock() => Show(_service.Add(AutomationKind.Block));

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
