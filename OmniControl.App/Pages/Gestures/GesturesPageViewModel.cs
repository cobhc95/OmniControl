using OmniControl.App.Dialogs;
using OmniControl.App.AppLists;
using OmniControl.App.Picker;
using OmniControl.App.Gestures;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Rules;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Gestures;

public sealed partial class GesturesPageViewModel : PageViewModel
{
    public override string Title => "Gestures";

    public override string? NavGroup => NavGroups.Workflow;

    private const string AdminNote = "Programs running as administrator are not affected unless OmniControl runs as administrator too.";

    public override string KeyHelp => string.Join(Environment.NewLine,
        "Left/Right move between a gesture's switch and its action, and across a pair; Space flips a switch; Enter on the action opens the action picker.",
        "Edge strip width: Left/Right move between the slider and the Show margins switch; Enter on the slider lets Left/Right change it.",
        "Custom rules: Space flips a rule's switch; Enter on Edit opens it.",
        "Rule editor: " + RuleEditorViewModel.KeyHelp,
        "Action picker: " + PickerModel.KeyHelp);
    public override bool ShowsOwnHeader => true; // the on/off switch sits on the title line
    public override string Icon => "M12,2 L17,8 H13 V11 H11 V8 H7 Z M12,22 L7,16 H11 V13 H13 V16 H17 Z";
    public override string Keywords => "gestures mouse wheel scroll middle click taskbar edge title bar desktop hold key volume brightness opacity zoom";

    private readonly ToggleItem _enabled;

    /// <summary>The on/off switch, shown beside the title.</summary>
    public IReadOnlyList<object> Primary { get; }

    public ItemGroup Areas { get; }

    /// <summary>The apps the screen-area gestures work in: the "Apps: ..." chip on the Areas group's title line.</summary>
    public AppScopeModel AreasScope { get; }
    public ItemGroup Middle { get; }
    public ItemGroup Keys { get; }

    /// <summary>The custom rules, last on the page.</summary>
    public RulesGroup Rules { get; } = new() { ExpandedByDefault = true };

    /// <summary>The groups in page order.</summary>
    private readonly IReadOnlyList<ItemGroup> _groups;

    /// <summary>Every setting in page order; a card also matches its group's name, so "middle click" finds the middle-click cards. Each rule is found by its name and sentence.</summary>
    public override IEnumerable<SearchEntry> SearchEntries =>
        Primary.Select(i => GestureEntry(i, string.Empty)).Concat(_groups.SelectMany(g => g.Items.Select(i => GestureEntry(i, g.Name))))
            .Concat(Rules.Rules.Select(r => Entry(r, r.Label, $"custom rule gesture {r.Line}", Rules.Name)));

    private SearchEntry GestureEntry(object editor, string group) => Entry(editor, Title, $"gesture mouse wheel scroll {group}");

    private readonly List<GestureItem> _gestureItems = new();
    private readonly GestureService _gestures;
    private readonly RuleService _rules;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    // The gesture areas drawn on the screen: only while "Show margins" is on and this page can be seen. Always off at start-up.
    private readonly MarginOverlay _margins;
    private readonly ToggleItem _showMargins;
    private readonly SliderItem _edgeStrip;
    private bool _onScreen;

    /// <param name="ask">Shows a short message with a confirm button (a rule's command, deleting a rule); true when confirmed.</param>
    /// <param name="scopes">Makes the apps scope models of the screen-area group and its cards.</param>
    public GesturesPageViewModel(GestureService gestures, RuleService rules, MarginOverlay margins, HotkeyRegistry hotkeys, Func<DialogMessage, Task<bool>> ask, AppScopes scopes)
    {
        _gestures = gestures;
        _rules = rules;
        _ask = ask;
        _margins = margins;
        _showMargins = new ToggleItem("Show margins", false, _ => UpdateMargins());
        var settings = gestures.Settings;
        _enabled = new ToggleItem("Enable gestures", settings.Enabled, gestures.SetEnabled) { Half = true };

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
        AreasScope = scopes.Group($"{Title} › {GestureCatalog.AreaGroup}", () => settings.AreasScope, gestures.SetAreasScope);
        List<object> CardsFor(string group, Func<IReadOnlyList<PickChoice>> choices) => GestureCatalog.Triggers.Where(t => t.Group == group)
            .Select(t =>
            {
                var rule = settings.RuleFor(t);
                var scope = group != GestureCatalog.AreaGroup ? null : scopes.Card($"{Title} › {group} › {t.Label}", AreasScope, () => rule.Scope, own => gestures.SetScope(t, own));
                var item = new GestureItem(t.Label, choices, rule, gestures.Apply, ApplyToOthers, scope);
                _gestureItems.Add(item);
                return (object)item;
            }).ToList();

        var strip = new SliderItem(GestureSettings.EdgeStripLabel, settings.EdgeStrip,
            v => { gestures.SetEdgeStrip(v); margins.Refresh(); return OperationResult.Ok(GestureSettings.EdgeStripLabel); }, _ => { },
            GestureSettings.MinEdgeStrip, GestureSettings.MaxEdgeStrip, GestureSettings.EdgeStripUnit, GestureSettings.DefaultEdgeStrip)
            { Companion = _showMargins };

        _edgeStrip = strip;
        Primary = new object[] { _enabled };
        var areaCards = CardsFor(GestureCatalog.AreaGroup, () => steps);
        areaCards.Add(strip);
        Areas = new ItemGroup("Gestures/Areas", GestureCatalog.AreaGroup, areaCards)
        {
            Tip = "Wheel up and down over the area do the action's two directions; a gesture that is on takes the scroll there. "
                  + "Show margins draws every area on screen while this page is open. " + AdminNote,
        };
        Middle = new ItemGroup("Gestures/Middle", GestureCatalog.MiddleGroup, CardsFor(GestureCatalog.MiddleGroup, () => clicks))
        {
            Tip = "A middle click over the area runs the action instead of the area's own middle click and the button's mapping.",
        };
        Keys = new ItemGroup("Gestures/Keys", GestureCatalog.KeyGroup, CardsFor(GestureCatalog.KeyGroup, () => steps))
        {
            Tip = "Keys held count exactly: Ctrl + Alt does not trigger Ctrl + Alt + Shift.",
        };
        _groups = new[] { Areas, Middle, Keys };

        foreach (var rule in rules.Settings.Rules) AddItem(rule);
        rules.ProblemsChanged += RefreshRules;
        gestures.Applied += RefreshRules; // a gesture switched on or off can start or end a rule's conflict

        hotkeys.Register(new HotkeyAction("gestures.toggle", "Toggle gestures", Title, "gestures mouse wheel scroll on off custom rules",
            _ => _enabled.IsOn = !_enabled.IsOn));
        hotkeys.Register(new HotkeyAction("gestures.margins", "Toggle gesture margins", Title, "gestures show margins edge strip width areas screen",
            _ => _showMargins.IsOn = !_showMargins.IsOn));
    }

    /// <summary>The Mouse and Trackpad page may have changed a mapping a rule overrides.</summary>
    public override void OnShown() => RefreshRules();

    private void RefreshRules()
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

    /// <summary>"Add rule": a new, empty rule at the end of the list, and its editor.</summary>
    public RuleEditorViewModel AddRule() => EditorFor(AddItem(_rules.Add()), isNew: true);

    private void RemoveItem(RuleItem item)
    {
        if (!Rules.Rules.Remove(item)) return;
        _rules.Remove(item.Rule);
    }

    public override void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        UpdateMargins();
    }

    private void UpdateMargins()
    {
        if (_showMargins.IsOn && _onScreen) _margins.Show();
        else _margins.Hide();
    }

    /// <summary>One gesture's settings were given to every gesture with the same action (the window's "apply to every gesture" switch).</summary>
    private void ApplyToOthers(GestureItem source, PickResult pick)
    {
        foreach (var item in _gestureItems.Where(i => i != source)) item.TakeSettingsOf(pick);
        _gestures.Apply();
    }
}
