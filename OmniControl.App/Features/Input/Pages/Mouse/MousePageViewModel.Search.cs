using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Pointer;
using OmniControl.Core.Engine.Trackpad;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Pages.Gestures;
using OmniControl.App.Features.Input.Rules;

namespace OmniControl.App.Features.Input.Pages.Mouse;

// What the page offers to global search: each card's words once, read by the page's results and, without making the page, by its word
// source (Words) from the catalogues and the services.
public sealed partial class MousePageViewModel
{
    private const string MouseLayerLabel = "Mouse layer", TrackpadLayerLabel = "Trackpad layer";

    public override IEnumerable<SearchEntry> SearchEntries =>
        new[] { Entry(MouseLayer, Card(MouseLayer.Label, ButtonKeywords, ButtonsGroupName)) }
            .Concat(ButtonsGroup.Items.Select(i => Entry(i, Card(LabelOf(i), WithActionNames(i, ButtonKeywords), ButtonsGroupName))))
            .Append(Entry(TrackpadLayer, Card(TrackpadLayer.Label, TrackpadKeywords, TrackpadGroupName)))
            .Concat(TrackpadGroup.Items.Select(i => Entry(i, Card(LabelOf(i), WithActionNames(i, TrackpadKeywords), TrackpadGroupName))))
            .Concat(Gestures.Groups.SelectMany(g => g.Items.Select(i => Entry(i, Card(LabelOf(i), GestureKeywordsOf(g.Name), g.Name)))))
            .Concat(Gestures.Rules.Rules.Select(r => Entry(r, RuleCard(r.Label, r.Line))))
            .Concat(WhereGroup.Items.Select(i => Entry(i, Card(LabelOf(i), WhereKeywords, WhereGroupName))))
            .Concat(_pointerSearch.Select(e => Entry(e.Card, Card(LabelOf(e.Card), $"{PointerActions.Keywords} {e.Keywords}", PointerControls.GroupName))));

    /// The page's search words (<c>FeaturePage.Words</c>). A button or trackpad gesture card is found by its actions' names: here every name
    /// the action lists offer and every action stored for it on any layer, so whatever it shows is in its words.
    public static IEnumerable<string> Words(MouseService? mouse, IActionRegistry? hotkeys, RuleService? rules)
    {
        var offered = hotkeys is null ? string.Empty : string.Join(" ", ActionChoices(hotkeys).Select(c => c.Name).Distinct().Append(ActionSlot.MissingName));
        string Actions(MouseButton button) => mouse is null ? offered
            : $"{offered} {string.Join(" ", Enumerable.Range(1, MouseSettings.Layers).SelectMany(layer => mouse.Settings.ActionsOf(layer, button)))}";
        var pointerMore = $"{PointerActions.Keywords} {PointerMoreKeywords}";
        return new[] { Card(MouseLayerLabel, ButtonKeywords, ButtonsGroupName) }
            .Concat(Buttons.Select(b => Card(b.Label, $"{ButtonKeywords} {Actions(b.Button)}", ButtonsGroupName)))
            .Append(Card(TrackpadLayerLabel, TrackpadKeywords, TrackpadGroupName))
            .Concat(TrackpadGestures.All.Select(g => Card(g.Name, $"{TrackpadKeywords} {Actions(g.Button)}", TrackpadGroupName)))
            .Append(Card(TouchpadSettingsLabel, TrackpadKeywords, TrackpadGroupName))
            .Concat(GestureCatalog.Triggers.Select(t => Card(t.Label, GestureKeywordsOf(t.Group), t.Group)))
            .Append(Card(GestureSettings.EdgeStripLabel, GestureKeywordsOf(GestureCatalog.AreaGroup), GestureCatalog.AreaGroup))
            .Concat(rules?.Settings.Rules.Select(r => (Rule: r, Line: RuleItem.LineOf(r, rules))).Select(r => RuleCard(RuleItem.LabelOf(r.Rule, r.Line), r.Line))
                ?? Array.Empty<SearchCard>())
            .Concat(new[] { GestureGroups.EnabledLabel, PauseLabel, NeverLabel, AdministratorLabel, RemoteLabel }.Select(l => Card(l, WhereKeywords, WhereGroupName)))
            .Concat(PointerControls.All.Select(c => Card(c.Label, $"{PointerActions.Keywords} {c.Keywords}", PointerControls.GroupName)))
            .Concat(new[] { PointerSizeLabel, ScrollInactiveLabel, ResetDefaultsLabel, RestoreOriginalLabel }.Concat(CursorRoles.All.Select(r => r.Name))
                .Select(l => Card(l, pointerMore, PointerControls.GroupName)))
            .Select(c => c.Words).ToList();
    }

    // Every list a card's actions come from (MouseButtonItem's lists).
    private static IEnumerable<PickChoice> ActionChoices(IActionRegistry hotkeys) =>
        MouseActionCatalog.Choices(hotkeys.PickerActions)
            .Concat(MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false))
            .Concat(MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false, gesture: true))
            .Concat(MouseActionCatalog.ExtraChoices(hotkeys.PickerActions))
            .Concat(MouseActionCatalog.ExtraChoices(hotkeys.PickerActions, gesture: true));

    private static SearchCard Card(string label, string keywords, string group) => new(label, keywords, group, Info.Title);

    private static SearchCard RuleCard(string label, string line) => new(label, $"custom rule gesture {line}", RulesGroup.GroupName);

    private static string GestureKeywordsOf(string group) => $"gesture mouse wheel scroll {group}";

    private static string LabelOf(object item) => (item as IEditorItem)?.Label ?? string.Empty;

    // A card is also found by the names of its actions (search reads this each time it runs, so it is always current).
    private static string WithActionNames(object item, string keywords) => item is MouseButtonItem card ? $"{keywords} {card.SearchWords}" : keywords;
}
