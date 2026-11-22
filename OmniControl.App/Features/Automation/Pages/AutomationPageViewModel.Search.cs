using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation.Pages;

// What the Automation page offers to global search, defined once (see SearchBuilder): the page's own parts, the pickers' names and one card per
// rule, custom action and script, each with its live row when the page is made.
public sealed partial class AutomationPageViewModel
{
    private const string AutomationWords = "automation rule schedule timer task";

    private static readonly string PauseTipText = $"Stops every rule and block at once. {ReservedKeys.Emergency} does the same from anywhere.";

    private static readonly SearchCard AutomationsCard = new("Automations", AutomationWords + " when do trigger new", RulesTab)
        {
            Tip = "When something happens (and the conditions hold), do one or more actions. Each also becomes an action called Run: its name.",
        },
        BlocksCard = new("Blocks", AutomationWords + " while don't allow forbid new", RulesTab)
        {
            Tip = "While the conditions hold, the chosen actions are not allowed, whether a hotkey, a mouse button, a gesture or a rule starts them. Pausing all rules lifts every block.",
        },
        PresetRulesCard = new("Preset rules", AutomationWords + " preset switch when it ends previous comes back open presets window", PresetsTab)
        {
            Tip = "Rules made from the Presets window: when their triggers hold, a preset is applied, and when they stop, the preset in use before comes back. Edit them here or there; it is the same rule.",
        },
        CustomsCard = new("Custom actions", AutomationWords + " script command program url new", CustomTab)
        {
            Tip = "Your own actions: a command, a script, a program, a file or folder, a web address. They run as you (never as administrator), and appear in Shortcuts, the mouse and gesture pickers and every rule.",
        },
        ScriptsCard = new("Scripts", AutomationWords + " script new import export run", ScriptsTab)
        {
            Tip = "Scripts kept inside OmniControl. Each becomes an action, Run script: its name, for Shortcuts, the mouse, gestures and rules. They run as you (never as administrator), hidden, and are stopped after a minute.",
        },
        PauseCard = new("Pause all rules", AutomationWords + " resume stop emergency safe start turn rules on", Title: ActionSections.Automation) { Tip = PauseTipText },
        TriggersCard = new("What starts a rule", AutomationWords + " trigger when", RulesTab) { Choices = AutomationTriggers.FixedNames },
        ConditionsCard = new("Conditions", AutomationWords + " and or and not while", RulesTab) { Choices = AutomationConditions.FixedNames },
        CustomKindsCard = new("Custom action types", AutomationWords + " custom action kind type", CustomTab) { Choices = CustomKinds.Names },
        LanguagesCard = new("Script languages", AutomationWords + " script language", ScriptsTab) { Choices = Enum.GetValues<ScriptLanguage>().Select(ScriptLanguages.NameOf).ToList() };

    private static SearchCard RuleCard(AutomationRule rule, string line) =>
        new(rule.Name, $"{AutomationWords} {line}", rule.Preset.Length > 0 ? PresetsTab : RulesTab);

    private static SearchCard CustomCard(CustomAction custom) =>
        new(custom.Name, $"custom action script command program url {custom.Target}", CustomTab) { Choices = CustomKinds.Names, Note = custom.Target };

    private static SearchCard ScriptCard(Script script) => new(script.Name, $"script powershell cmd batch python {script.Name}", ScriptsTab) { Note = ScriptLanguages.NameOf(script.Language) };

    protected override SearchBuilder SearchCards => Cards(_service, this);

    /// THE definition of the page's search: its parts, the pickers' names, and the service's rules, custom actions and scripts with their rows.
    /// A rule's sentence is its row's own; without the page a row is made and let go for it, so the words never differ from the row's.
    private static SearchBuilder Cards(AutomationService? service, AutomationPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(PauseCard).Add(AutomationsCard).Add(BlocksCard).Add(PresetRulesCard).Add(CustomsCard).Add(ScriptsCard)
            .Add(TriggersCard).Add(ConditionsCard).Add(CustomKindsCard).Add(LanguagesCard);
        if (service is null) return builder;
        foreach (var rule in service.Settings.Rules)
        {
            var row = page?.RuleRows.FirstOrDefault(r => r.Rule == rule);
            if (row is not null) { builder.Add(RuleCard(rule, row.Line), row); continue; }
            using var temporary = new AutomationItem(rule, service, _ => Task.FromResult(false), null);
            builder.Add(RuleCard(rule, temporary.Line));
        }
        return builder
            .AddEach(service.Settings.Customs, CustomCard, c => page?.Customs.FirstOrDefault(r => r.Custom == c))
            .AddEach(service.Settings.Scripts, ScriptCard, s => page?.Scripts.FirstOrDefault(r => r.Script == s));
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(AutomationService? service) => Cards(service, null).Words;
}
