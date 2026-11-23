using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation.Pages;

// What the Automation page offers to global search, defined once (see SearchBuilder): the page's own parts, the pickers' names and one card per
// rule and script, each with its live row when the page is made.
public sealed partial class AutomationPageViewModel
{
    private const string AutomationWords = "automation rule schedule timer task";

    private const string FeatureRulesKept = "Rules made on other pages keep running while rules are paused.";

    private static readonly string PauseTipText = $"Stops every rule and block at once. {FeatureRulesKept} {ReservedKeys.Emergency} does the same from anywhere.";

    private static readonly SearchCard AutomationsCard = new("Automations", AutomationWords + " when do trigger new preset presets switch when it ends previous comes back presets manager", RulesTab)
        {
            Tip = $"When something happens (and the conditions hold), do one or more actions. Each also becomes an action called Run: its name. A rule that switches to a preset is made here too, or from its row in {Shared.Presets.Manager.PresetManagerService.Title}.",
        },
        BlocksCard = new("Blocks", AutomationWords + " while don't allow forbid new", RulesTab)
        {
            Tip = "While the conditions hold, the chosen actions are not allowed, whether a hotkey, a mouse button, a gesture or a rule starts them. Pausing all rules lifts every block.",
        },
        FeatureRulesCard = new("Rules made on other pages", AutomationWords + " other pages feature made on open warm light network limits device check emulator mouse button trackpad", FeatureTab)
        {
            Tip = $"Rules a page made for what it does by itself, such as warm light. They are ordinary rules: add triggers, conditions or actions here, and the page shows that it has more. " + FeatureRulesKept,
        },
        ScriptsCard = new("Scripts", AutomationWords + " script new import export run", ScriptsTab)
        {
            Tip = "Scripts kept inside OmniControl. Each becomes an action, Run script: its name, for Shortcuts, the mouse, gestures and rules. They run as you (never as administrator), hidden, and are stopped after a minute.",
        },
        PauseCard = new("Pause all rules", AutomationWords + " resume stop emergency safe start turn rules on", Title: ActionSections.Automation) { Tip = PauseTipText },
        TriggersCard = new("What starts a rule", AutomationWords + " trigger when and or and not while condition", RulesTab) { Choices = AutomationTriggers.FixedNames },
        LanguagesCard = new("Script languages", AutomationWords + " script language", ScriptsTab) { Choices = Enum.GetValues<ScriptLanguage>().Select(ScriptLanguages.NameOf).ToList() };

    private static SearchCard RuleCard(AutomationRule rule, string line, AutomationFeatureRules? features) =>
        new(rule.Name, $"{AutomationWords} {line}", features?.PageOf(rule) is not null ? FeatureTab : RulesTab);

    private static SearchCard ScriptCard(Script script) => new(script.Name, $"script powershell cmd batch python {script.Name}", ScriptsTab) { Note = ScriptLanguages.NameOf(script.Language) };

    protected override SearchBuilder SearchCards => Cards(_service, _features, this);

    /// THE definition of the page's search: its parts, the pickers' names, and the service's rules and scripts with their rows.
    /// A rule's sentence is its row's own; without the page a row is made and let go for it, so the words never differ from the row's.
    private static SearchBuilder Cards(AutomationService? service, AutomationFeatureRules? features, AutomationPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(PauseCard).Add(AutomationsCard).Add(BlocksCard).Add(FeatureRulesCard).Add(ScriptsCard)
            .Add(service is null ? TriggersCard : TriggersCard with { Choices = AutomationTriggers.FixedNames.Concat(service.TriggerSources.States.Select(s => s.Name)).ToList() })
            .Add(LanguagesCard);
        if (service is null) return builder;
        foreach (var rule in service.Settings.Rules)
        {
            var row = page?.RuleRows.FirstOrDefault(r => r.Rule == rule);
            if (row is not null) { builder.Add(RuleCard(rule, row.Line, features), row); continue; }
            using var temporary = new AutomationItem(rule, service, _ => Task.FromResult(false), null, null);
            builder.Add(RuleCard(rule, temporary.Line, features));
        }
        return builder
            .AddEach(service.Settings.Scripts, ScriptCard, s => page?.Scripts.FirstOrDefault(r => r.Script == s));
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(AutomationService? service, AutomationFeatureRules? features) => Cards(service, features, null).Words;
}
