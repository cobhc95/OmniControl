using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Rules;

namespace OmniControl.App.Features.Input.Pages.Gestures;

/// The gesture groups of the Mouse and Trackpad page (as the Devices page holds its import group): scroll over a screen area, middle
/// click over one, scroll while holding a key, each with its Apps chip, then the custom gesture rules. Also owns the "Enable gestures"
/// card the page's "All mouse and trackpad features" group shows, and the on-screen margins.
public sealed class GestureGroups
{
    private const string Where = ActionSections.Mouse;
    private const string AdminNote = "Programs running as administrator are not affected unless OmniControl runs as administrator too.";

    public const string KeyHelp =
        "Gestures: Left/Right move between a gesture's switch and its action, and across a pair; Space flips a switch; Enter on the action opens the action picker. "
        + "Edge strip width: Left/Right move between the slider and the Show margins switch; Enter on the slider lets Left/Right change it. "
        + "Custom gesture rules: Space flips a rule's switch; Enter on Edit opens it.";

    /// "Enable gestures": every wheel and middle-click gesture and the custom rules.
    public ToggleItem Enabled { get; }

    public ItemGroup Areas { get; }
    public ItemGroup Middle { get; }
    public ItemGroup Keys { get; }

    /// The apps each group works in: the "Apps: ..." chip on its title line.
    public AppScopeModel AreasScope { get; }
    public AppScopeModel MiddleScope { get; }
    public AppScopeModel KeysScope { get; }

    public RulesGroup Rules { get; } = new() { ExpandedByDefault = true };

    /// The card groups in page order (search reads them).
    public IReadOnlyList<ItemGroup> Groups { get; }

    private readonly List<GestureItem> _gestureItems = new();
    private readonly GestureService _gestures;
    private readonly RuleService _rules;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    // The gesture areas drawn on the screen: only while "Show margins" is on and the page can be seen. Always off at start-up.
    private readonly MarginOverlay _margins;
    private readonly ToggleItem _showMargins;
    private readonly SliderItem _edgeStrip;
    private bool _onScreen;

    /// <param name="ask">Shows a short message with a confirm button (a rule's command, deleting a rule); true when confirmed.</param>
    /// <param name="scopes">Makes the apps scope models of the groups and their cards.</param>
    public GestureGroups(GestureService gestures, RuleService rules, MarginOverlay margins, IActionRegistry hotkeys, Func<DialogMessage, Task<bool>> ask, AppScopes scopes)
    {
        _gestures = gestures;
        _rules = rules;
        _ask = ask;
        _margins = margins;
        _showMargins = new ToggleItem("Show margins", false, _ => UpdateMargins());
        var settings = gestures.Settings;
        Enabled = new ToggleItem("Enable gestures", settings.Enabled, gestures.SetEnabled)
        {
            Half = true,
            Tip = "Every scroll and middle-click gesture and the custom gesture rules.",
        };

        // The action lists follow the master list: a level or action a feature registers later appears in every gesture's list.
        var steps = GestureCatalog.Choices(hotkeys.PickerActions);
        var clicks = GestureCatalog.ClickChoices(hotkeys.PickerActions);
        hotkeys.Actions.CollectionChanged += (_, _) =>
        {
            steps = GestureCatalog.Choices(hotkeys.PickerActions);
            clicks = GestureCatalog.ClickChoices(hotkeys.PickerActions);
            foreach (var item in _gestureItems) item.Reload();
            RefreshRules(); // a rule's sentence names its actions
        };
        AppScopeModel ScopeOf(string group) => scopes.Group($"{Where} › {group}", () => settings.GroupScopeOf(group), s => gestures.SetGroupScope(group, s));
        List<object> CardsFor(string group, AppScopeModel groupScope, Func<IReadOnlyList<PickChoice>> choices) => GestureCatalog.Triggers.Where(t => t.Group == group)
            .Select(t =>
            {
                var rule = settings.RuleFor(t);
                var scope = scopes.Card($"{Where} › {group} › {t.Label}", groupScope, () => rule.Scope, own => gestures.SetScope(t, own));
                var item = new GestureItem(t.Label, choices, rule, gestures.Apply, ApplyToOthers, scope);
                _gestureItems.Add(item);
                return (object)item;
            }).ToList();

        _edgeStrip = new SliderItem(GestureSettings.EdgeStripLabel, settings.EdgeStrip,
            v => { gestures.SetEdgeStrip(v); margins.Refresh(); return OperationResult.Ok(GestureSettings.EdgeStripLabel); }, _ => { },
            GestureSettings.MinEdgeStrip, GestureSettings.MaxEdgeStrip, GestureSettings.EdgeStripUnit, GestureSettings.DefaultEdgeStrip)
            { Companion = _showMargins };

        AreasScope = ScopeOf(GestureCatalog.AreaGroup);
        MiddleScope = ScopeOf(GestureCatalog.MiddleGroup);
        KeysScope = ScopeOf(GestureCatalog.KeyGroup);
        var areaCards = CardsFor(GestureCatalog.AreaGroup, AreasScope, () => steps);
        areaCards.Add(_edgeStrip);
        Areas = new ItemGroup("Gestures/Areas", GestureCatalog.AreaGroup, areaCards)
        {
            Tip = "Wheel up and down over the area do the action's two directions; a gesture that is on takes the scroll there. "
                  + "Show margins draws every area on screen while this page is open. " + AdminNote,
        };
        Middle = new ItemGroup("Gestures/Middle", GestureCatalog.MiddleGroup, CardsFor(GestureCatalog.MiddleGroup, MiddleScope, () => clicks))
        {
            Tip = "A middle click over the area runs the action instead of the area's own middle click and the button's mapping.",
        };
        Keys = new ItemGroup("Gestures/Keys", GestureCatalog.KeyGroup, CardsFor(GestureCatalog.KeyGroup, KeysScope, () => steps))
        {
            Tip = "Keys held count exactly: Ctrl + Alt does not trigger Ctrl + Alt + Shift.",
        };
        Groups = new[] { Areas, Middle, Keys };

        foreach (var rule in rules.Settings.Rules) AddItem(rule);
        rules.ProblemsChanged += RefreshRules;
        gestures.Applied += RefreshRules; // a gesture switched on or off can start or end a rule's conflict

        hotkeys.Register(new HotkeyAction("gestures.toggle", "Toggle gestures", Where, "gestures mouse wheel scroll on off custom rules",
            _ => Enabled.IsOn = !Enabled.IsOn));
        hotkeys.Register(new HotkeyAction("gestures.margins", "Toggle gesture margins", Where, "gestures show margins edge strip width areas screen",
            _ => _showMargins.IsOn = !_showMargins.IsOn));
    }

    public GestureSettings CurrentGestures => _gestures.Settings;
    public RuleSettings CurrentRules => _rules.Settings;

    /// The page was opened: the button mappings may have changed what a rule overrides.
    public void RefreshRules()
    {
        foreach (var item in Rules.Rules) item.Refresh();
    }

    private RuleItem AddItem(Rule rule)
    {
        var item = new RuleItem(rule, _rules, i => EditorFor(i, isNew: false));
        Rules.Rules.Add(item);
        return item;
    }

    private RuleEditorViewModel EditorFor(RuleItem item, bool isNew) =>
        new(item.Rule, isNew, _rules, _ask, item.Refresh, () => RemoveItem(item), () =>
        {
            // A rule added and closed again without anything chosen was not wanted.
            if (isNew && item.Rule.Trigger.Length == 0 && item.Rule.Conditions.Count == 0 && item.Rule.Steps.Count == 0) RemoveItem(item);
        });

    /// "Add rule": a new, empty rule at the end of the list, and its editor.
    public RuleEditorViewModel AddRule() => EditorFor(AddItem(_rules.Add()), isNew: true);

    private void RemoveItem(RuleItem item)
    {
        if (!Rules.Rules.Remove(item)) return;
        _rules.Remove(item.Rule);
    }

    public void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        UpdateMargins();
    }

    private void UpdateMargins()
    {
        if (_showMargins.IsOn && _onScreen) _margins.Show();
        else _margins.Hide();
    }

    /// The "All mouse and trackpad features" group's Restore defaults: Enable gestures back on.
    public void RestoreEnabled() => Enabled.IsOn = GestureSettings.DefaultEnabled;

    /// Puts the built-in gestures and the custom rules in place ("Restore defaults" and its undo) and shows them.
    public void Replace(GestureSettings gestures, RuleSettings rules)
    {
        _gestures.Replace(gestures);
        _rules.ReplaceAll(rules);
        Enabled.Load(_gestures.Settings.Enabled);
        _edgeStrip.Load(_gestures.Settings.EdgeStrip);
        _margins.Refresh();
        AreasScope.Refresh();
        MiddleScope.Refresh();
        KeysScope.Refresh();
        foreach (var item in _gestureItems) item.Reload();
        Rules.Rules.Clear();
        foreach (var rule in _rules.Settings.Rules) AddItem(rule);
        RefreshRules();
    }

    /// One gesture's settings were given to every gesture with the same action (the window's "apply to every gesture" switch).
    private void ApplyToOthers(GestureItem source, PickResult pick)
    {
        foreach (var item in _gestureItems.Where(i => i != source)) item.TakeSettingsOf(pick);
        _gestures.Apply();
    }
}
