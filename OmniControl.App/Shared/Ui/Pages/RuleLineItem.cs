using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE muted line a feature's page shows under the controls of something it does through an automation rule, while that rule has more than the
/// page shows: "Runs as an automation rule (1 extra condition) * · Edit in Automation" (the asterisk's tooltip: More in Automation; the link
/// only while Automation's page is there).
public sealed partial class RuleLineItem : ObservableObject, IEditorItem
{
    public const string Lead = "Runs as an automation rule", EditWords = "Edit in Automation", MoreTip = "More in Automation";

    public string Label => Lead;

    /// The line's words ("Runs as an automation rule (1 extra condition)"); hidden while the rule has nothing extra.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Shown))] private string _words = string.Empty;

    [ObservableProperty] private bool _canEdit;

    public bool Shown => Words.Length > 0;

    public IRelayCommand EditCommand { get; }

    public RuleLineItem(Action edit) => EditCommand = new RelayCommand(edit);

    /// Shows what was added to the rule in Automation (none or nothing added: the line hides).
    public void Show(FeatureRuleView? rule)
    {
        Words = rule is { Extras.Length: > 0 } ? $"{Lead} ({rule.Extras})" : string.Empty;
        CanEdit = rule?.Editable == true;
    }

    public IEnumerable<string> SearchTexts => new[] { Lead, EditWords };
}
