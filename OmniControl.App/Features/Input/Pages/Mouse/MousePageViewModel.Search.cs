using OmniControl.App.Features.AppLists;
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

// What the page offers to global search, defined once (see SearchBuilder): every card's words come from the catalogues and the services, and
// each card has its live item when the page is made, so the page's results and its word source (Words) are the same list.
public sealed partial class MousePageViewModel
{
    private const string MouseLayerLabel = "Mouse layer", TrackpadLayerLabel = "Trackpad layer";
    private const string PointerMoreKeywords = "pointer shape scheme file size colour color scroll inactive windows reset restore defaults";

    private const string ButtonsTip = "A button left at Default works as Windows normally does. The layer drop-down shows the mouse layer in force and changes it (the trackpad "
        + "has its own); a button set to \"Hold for layer N\" uses layer N while held. Over OmniControl's own window the left and right buttons are never remapped. Windows does "
        + "not pass mouse input to programs running as administrator unless OmniControl runs as administrator too.";

    private const string TrackpadTip = "Swipes, pinches and taps on a precision touchpad; Default lets Windows do what it normally does. "
        + "The layer drop-down on this line picks the trackpad's own layer, separate from the mouse's. "
        + "Outside its apps a gesture does nothing; Windows' own gesture stays off while the gesture has an action. "
        + "OmniControl can't block Windows' own gesture, so while a gesture has an action it turns Windows' one off and gives it back afterwards; "
        + "tick \"Also let Windows do its own gesture\" in an action's settings to keep both. "
        + "A double tap replaces the second tap of the pair. One-finger taps are also clicks, so give that card an action with care. Two-finger scrolling arrives as Wheel and Tilt.";

    private const string PointerTip = "Windows' own mouse settings: they change at once, stay after a restart and show what Windows has now. Your values from before OmniControl's "
        + "first change are kept, so Restore can put them all back. Swapping the buttons asks to be kept and goes back by itself if you don't.";

    private const string WhereRestoreTip = "Turns gestures on and puts pause in full-screen games, the apps to never act in, run as administrator and sending actions to remote PCs as keys back to their defaults.";

    private static readonly string RemoteTip = "While a remote desktop or virtual machine window is in front, a button's or trackpad gesture's action that has a standard shortcut "
        + "(volume, play or pause, next track, close window) is sent as that shortcut, so the other PC acts; every shortcut is typed like a "
        + "keyboard does. Where a Remote Desktop keeps shortcuts on this PC (its \"Apply Windows key combinations\" is \"On this computer\", or "
        + "\"Only when using the full screen\" while it is not full screen), they go as its own replacements ("
        + string.Join(", ", RemoteInputApps.All.SelectMany(a => a.LocalSwaps ?? Array.Empty<RemoteInputApps.KeySwap>()).Distinct().Select(s => $"{s.Keys} as {s.SentAs}"))
        + "); Windows-key shortcuts (Task view, Show desktop, snap, virtual desktops) have none and then act on this PC. Works in: "
        + string.Join(", ", RemoteInputApps.All.Select(a => a.Name)) + ".";

    private static readonly SearchCard FullScreenCard = new("Pause in full-screen games", WhereKeywords, WhereGroupName, ActionSections.Mouse)
        {
            Note = "Any full-screen app counts, videos too",
            Tip = "While a full-screen app is in front, every mouse button, wheel step and gesture goes to it untouched.",
        },
        NeverCard = new("Never in these apps", WhereKeywords, WhereGroupName, ActionSections.Mouse)
        {
            Note = "Overrides every group's Apps choice",
            Tip = "In these apps buttons, trackpad gestures, gestures and the autoscroll block do nothing.",
        },
        AdministratorCard = new("Run as administrator", WhereKeywords, WhereGroupName, ActionSections.Mouse)
        {
            Note = "Needed over admin windows",
            Tip = "Turning it on starts OmniControl again as administrator. Windows asks at each start unless Setup's \"Start OmniControl as administrator at sign-in\" was ticked.",
        },
        RemoteCard = new("Send actions to remote PCs as keys", WhereKeywords, WhereGroupName, ActionSections.Mouse)
        {
            Note = "Remote Desktop, virtual machines",
            Tip = RemoteTip,
        },
        EnabledCard = new(GestureGroups.EnabledLabel, WhereKeywords, WhereGroupName, ActionSections.Mouse) { Tip = GestureGroups.EnabledTip },
        TouchpadSettingsCard = new("Windows touchpad settings", TrackpadKeywords, TrackpadGroupName, ActionSections.Mouse) { Note = "Windows' own swipes, pinch and taps." },
        PointerSizeCard = new("Pointer size and colour", $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Note = "Set in Windows' Accessibility settings." },
        ScrollInactiveCard = new("Scroll inactive windows", $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Note = "Set in Windows' mouse settings." },
        RestoreOriginalCard = new("Restore my original values", $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Note = "Back to before OmniControl changed them." },
        ResetDefaultsCard = new("Reset to Windows defaults", $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Note = "Back to how Windows starts." },
        ButtonsGroupCard = new(ButtonsGroupName, ButtonKeywords, ButtonsGroupName, ActionSections.Mouse) { Tip = ButtonsTip },
        TrackpadGroupCard = new(TrackpadGroupName, TrackpadKeywords, TrackpadGroupName, ActionSections.Mouse) { Tip = TrackpadTip },
        PointerGroupCard = new(PointerControls.GroupName, $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Tip = PointerTip },
        WhereGroupCard = new(WhereGroupName, WhereKeywords, WhereGroupName, ActionSections.Mouse) { Tip = WhereRestoreTip },
        ScopeCard = new("Apps scope", $"{ButtonKeywords} apps scope chip", ButtonsGroupName, ActionSections.Mouse) { Menu = AppScopeModel.MenuWords },
        AreasGroupCard = new(GestureCatalog.AreaGroup, GestureKeywordsOf(GestureCatalog.AreaGroup), GestureCatalog.AreaGroup, ActionSections.Mouse) { Tip = GestureGroups.AreasTip },
        MiddleGroupCard = new(GestureCatalog.MiddleGroup, GestureKeywordsOf(GestureCatalog.MiddleGroup), GestureCatalog.MiddleGroup, ActionSections.Mouse) { Tip = GestureGroups.MiddleTip },
        KeysGroupCard = new(GestureCatalog.KeyGroup, GestureKeywordsOf(GestureCatalog.KeyGroup), GestureCatalog.KeyGroup, ActionSections.Mouse) { Tip = GestureGroups.KeysTip };

    private static SearchCard PointerCard(PointerControl control) => new(control.Label, $"{PointerActions.Keywords} {control.Keywords}", PointerControls.GroupName, ActionSections.Mouse);

    private static SearchCard RoleCard(CursorRole role) =>
        new(role.Name, $"{PointerActions.Keywords} {PointerMoreKeywords}", PointerControls.GroupName, ActionSections.Mouse) { Note = "Built-in pointer" };

    private static SearchCard RuleCard(string label, string line) => new(label, $"custom rule gesture {line}", RulesGroup.GroupName, ActionSections.Mouse);

    private static string GestureKeywordsOf(string group) => $"gesture mouse wheel scroll {group}";

    protected override SearchBuilder SearchCards => Cards(_mouse, _hotkeys, null, this);

    /// THE definition of the page's search: the group tips, the buttons, trackpad gestures, gesture triggers and rules, the "All mouse and
    /// trackpad features" switches and the Pointer settings, each with its live card when <paramref name="page"/> is given. A button or
    /// trackpad card is found by its actions' names: from the page, the card's own live ones; without it, every name the action lists offer
    /// and every action stored for it on any layer.
    private static SearchBuilder Cards(MouseService? mouse, IActionRegistry? hotkeys, RuleService? rules, MousePageViewModel? page)
    {
        var offered = page is null && hotkeys is not null ? string.Join(" ", ActionChoices(hotkeys).Select(c => c.Name).Distinct().Append(ActionSlot.MissingName)) : string.Empty;
        string Actions(MouseButton button) => mouse is null ? offered
            : $"{offered} {string.Join(" ", Enumerable.Range(1, MouseSettings.Layers).SelectMany(layer => mouse.Settings.ActionsOf(layer, button)))}";
        object? Live(IEnumerable<object>? items, string label) => items?.OfType<IEditorItem>().FirstOrDefault(i => i.Label == label);
        object? Button(MouseButton button) => page?._buttonItems.FirstOrDefault(i => i.Button == button);
        object? Gesture(string group, string label) => Live(page?.Gestures.Groups.FirstOrDefault(g => g.Name == group)?.Items, label);
        var builder = new SearchBuilder().Add(ButtonsGroupCard).Add(TrackpadGroupCard).Add(PointerGroupCard).Add(WhereGroupCard)
            .Add(ScopeCard).Add(AreasGroupCard).Add(MiddleGroupCard).Add(KeysGroupCard)
            .Add(LayerCard(mouse, InputFamily.Mouse, MouseLayerLabel, ButtonKeywords, ButtonsGroupName), page?.MouseLayer)
            .AddEach(Buttons, b => new SearchCard(b.Label, $"{ButtonKeywords} {Actions(b.Button)}", ButtonsGroupName, ActionSections.Mouse), b => Button(b.Button))
            .Add(LayerCard(mouse, InputFamily.Trackpad, TrackpadLayerLabel, TrackpadKeywords, TrackpadGroupName), page?.TrackpadLayer)
            .AddEach(TrackpadGestures.All, g => new SearchCard(g.Name, $"{TrackpadKeywords} {Actions(g.Button)}", TrackpadGroupName, ActionSections.Mouse), g => Button(g.Button))
            .Add(TouchpadSettingsCard, Live(page?.TrackpadGroup.Items, TouchpadSettingsCard.Label))
            .AddEach(GestureCatalog.Triggers, t => new SearchCard(t.Label, GestureKeywordsOf(t.Group), t.Group, ActionSections.Mouse), t => Gesture(t.Group, t.Label))
            .Add(new SearchCard(GestureSettings.EdgeStripLabel, GestureKeywordsOf(GestureCatalog.AreaGroup), GestureCatalog.AreaGroup, ActionSections.Mouse),
                Gesture(GestureCatalog.AreaGroup, GestureSettings.EdgeStripLabel));
        if (page is not null) builder.AddEach(page.Gestures.Rules.Rules, r => RuleCard(r.Label, r.Line), r => r);
        else if (rules is not null)
            builder.AddEach(rules.Settings.Rules.Select(r => (Rule: r, Line: RuleItem.LineOf(r, rules))), r => RuleCard(RuleItem.LabelOf(r.Rule, r.Line), r.Line));
        var where = page?.WhereGroup.Items;
        builder.Add(EnabledCard, Live(where, EnabledCard.Label)).Add(FullScreenCard, Live(where, FullScreenCard.Label)).Add(NeverCard, Live(where, NeverCard.Label))
            .Add(AdministratorCard, Live(where, AdministratorCard.Label)).Add(RemoteCard, Live(where, RemoteCard.Label));
        object? Pointer(string label) => page?._pointerItems.FirstOrDefault(i => i.Label == label);
        return builder
            .AddEach(PointerControls.All, PointerCard, c => Pointer(c.Label))
            .Add(PointerSizeCard, Pointer(PointerSizeCard.Label)).Add(ScrollInactiveCard, Pointer(ScrollInactiveCard.Label))
            .AddEach(CursorRoles.All, RoleCard, r => Pointer(r.Name))
            .Add(ResetDefaultsCard, Pointer(ResetDefaultsCard.Label)).Add(RestoreOriginalCard, Pointer(RestoreOriginalCard.Label));
    }

    // The layers the drop-down lists now, by their names.
    private static IReadOnlyList<string> LayerNames(MouseService mouse, InputFamily family)
    {
        var layers = mouse.Settings.LayersOf(family);
        var labels = layers.Labels();
        return layers.Listed(mouse.LayerOf(family)).Select(n => labels[n]).ToList();
    }

    private static SearchCard LayerCard(MouseService? mouse, InputFamily family, string label, string keywords, string group) =>
        new(label, keywords, group, ActionSections.Mouse)
        {
            Choices = mouse is null ? Array.Empty<string>() : LayerNames(mouse, family),
            Menu = new[] { ProviderChoice.ManageText },
            Tip = LayerPresets.OnlyUsedLabel + ": " + LayerPresets.OnlyUsedTip,
        };

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(MouseService? mouse, IActionRegistry? hotkeys, RuleService? rules) => Cards(mouse, hotkeys, rules, null).Words;

    // Every list a card's actions come from (MouseButtonItem's lists).
    private static IEnumerable<PickChoice> ActionChoices(IActionRegistry hotkeys) =>
        MouseActionCatalog.Choices(hotkeys.PickerActions)
            .Concat(MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false))
            .Concat(MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false, gesture: true))
            .Concat(MouseActionCatalog.ExtraChoices(hotkeys.PickerActions))
            .Concat(MouseActionCatalog.ExtraChoices(hotkeys.PickerActions, gesture: true));
}
