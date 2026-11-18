using OmniControl.App.Dialogs;
using Avalonia.Threading;
using OmniControl.App.AppLists;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.Osd;
using OmniControl.App.Presets;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The Mouse and Trackpad page: the Mouse Buttons group (mouse layer picker on its header), the Trackpad group
/// (<c>MousePageViewModel.Trackpad.cs</c>, trackpad layer picker on its header) and the Pointer settings group
/// (<c>MousePageViewModel.Pointer.cs</c>, its actions in <c>.PointerActions.cs</c>). The first two share the action list and the mappings
/// file, each with its own layers (<see cref="InputFamily"/>); the third changes Windows' own mouse settings.
/// </summary>
public sealed partial class MousePageViewModel : PageViewModel
{
    public override string Title => ActionSections.Mouse;

    public override string? NavGroup => NavGroups.Devices;

    public override string KeyHelp => string.Join(Environment.NewLine,
        "A pair of half-width cards reads left, then right. Enter on an action (or its cogwheel) opens the action picker.",
        "Action picker: " + PickerModel.KeyHelp);
    public override string Icon => "M12,2 C8.13,2 5,5.13 5,9 V15 C5,18.87 8.13,22 12,22 C15.87,22 19,18.87 19,15 V9 C19,5.13 15.87,2 12,2 Z M11,5 H13 V10 H11 Z M5,11.5 H19 V13 H5 Z";
    public override string Keywords => "mouse buttons remap layer wheel tilt x-mouse button control side button middle click " + TrackpadKeywords + " " + PointerKeywords + " " + WhereKeywords;

    private static readonly (MouseButton Button, string Label)[] Buttons =
    {
        (MouseButton.Left, "Left button"), (MouseButton.Right, "Right button"), (MouseButton.Middle, "Middle button"),
        (MouseButton.Back, "Back (button 4)"), (MouseButton.Forward, "Forward (button 5)"),
        (MouseButton.WheelUp, "Wheel up"), (MouseButton.WheelDown, "Wheel down"),
        (MouseButton.TiltLeft, "Tilt left"), (MouseButton.TiltRight, "Tilt right"),
    };

    private readonly MouseService _mouse;
    private const string ButtonKeywords = "mouse buttons remap layer preset apps scope";
    private const string ButtonsGroupName = "Mouse Buttons";

    private readonly List<MouseButtonItem> _buttonItems = new(); // every input's card: mouse buttons, then trackpad gestures
    private readonly LayerPicker _mouseLayers;
    private readonly LayerPicker _trackpadLayers;

    /// <summary>The nine button cards, in a collapsible group.</summary>
    public ItemGroup ButtonsGroup { get; }

    /// <summary>The apps the Mouse Buttons group works in: the "Apps: ..." chip on its title line, left of the layer picker.</summary>
    public AppScopeModel ButtonsScope { get; }

    /// <summary>The mouse layer picker, shown as a bare drop-down on the Mouse Buttons group's title line; it edits the button cards only.</summary>
    public ChoiceItem MouseLayer => _mouseLayers.Choice;

    /// <summary>The trackpad layer picker, the same drop-down on the Trackpad group's title line; it edits the gesture cards only.</summary>
    public ChoiceItem TrackpadLayer => _trackpadLayers.Choice;

    public override IEnumerable<SearchEntry> SearchEntries =>
        new[] { Entry(MouseLayer, Title, ButtonKeywords, ButtonsGroup.Name) }
            .Concat(ButtonsGroup.Items.Select(i => Entry(i, Title, WithActionNames(i, ButtonKeywords), ButtonsGroup.Name)))
            .Append(Entry(TrackpadLayer, Title, TrackpadKeywords, TrackpadGroup.Name))
            .Concat(TrackpadGroup.Items.Select(i => Entry(i, Title, WithActionNames(i, TrackpadKeywords), TrackpadGroup.Name)))
            .Concat(WhereSearchEntries())
            .Concat(PointerSearchEntries());

    private MouseSettings Settings => _mouse.Settings;

    // A card is also found by the names of its actions (search reads this each time it runs, so it is always current).
    private static string WithActionNames(object item, string keywords) => item is MouseButtonItem card ? $"{keywords} {card.SearchWords}" : keywords;

    public const string MouseLayersId = LayerPresets.MouseGroupId, TrackpadLayersId = LayerPresets.TrackpadGroupId;

    private readonly Action<string?, string?> _openPresets;

    /// <param name="ask">Shows a short message with a confirm button (the button-swap "Keep" question).</param>
    /// <param name="presets">The master list of preset groups: the page registers its mouse and trackpad layers.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group (the header cogwheels).</param>
    /// <param name="scopes">Makes the apps scope models of the Mouse Buttons group and its cards.</param>
    public MousePageViewModel(MouseService mouse, TrackpadService trackpad, PointerSettingsService pointer, HotkeyRegistry hotkeys, OsdService osd,
        Func<DialogMessage, Task<bool>> ask, Action<string> report, PresetGroups presets, Action<string?, string?> openPresets, AppScopes scopes)
    {
        _openPresets = openPresets;
        _mouse = mouse;
        _trackpad = trackpad;
        _pointer = pointer;
        _osd = osd;
        _ask = ask;
        _report = report;

        _mouseLayers = new LayerPicker(mouse, InputFamily.Mouse, "Mouse layer", () => ReloadCards(InputFamily.Mouse), ManageMouseLayers);
        _trackpadLayers = new LayerPicker(mouse, InputFamily.Trackpad, "Trackpad layer", () => ReloadCards(InputFamily.Trackpad), ManageTrackpadLayers);

        // The action list follows the master list: an action a feature registers later appears on every card's list. Inputs without a
        // release (wheel, tilt, gestures) get the list with the "while held" actions greyed; a trackpad gesture's settings word "keep the
        // original" as Windows' own gesture.
        // The second and later actions of a card run once each (one-shot lists, MouseActionCatalog.ExtraChoices).
        List<PickChoice> pressed = new(), fired = new(), gestures = new(), laterButtons = new(), laterGestures = new();
        void BuildLists()
        {
            pressed = MouseActionCatalog.Choices(hotkeys.PickerActions);
            fired = MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false);
            gestures = MouseActionCatalog.Choices(hotkeys.PickerActions, hasRelease: false, gesture: true);
            laterButtons = MouseActionCatalog.ExtraChoices(hotkeys.PickerActions);
            laterGestures = MouseActionCatalog.ExtraChoices(hotkeys.PickerActions, gesture: true);
        }
        BuildLists();
        hotkeys.Actions.CollectionChanged += (_, _) =>
        {
            BuildLists();
            ReloadButtons();
        };
        ButtonsScope = scopes.Group($"{Title} › {ButtonsGroupName}", () => Settings.ButtonsScope, scope =>
        {
            Settings.ButtonsScope = scope;
            _mouse.Apply();
        });
        MouseButtonItem CardFor(MouseButton button, string label, bool scoped) => new(button, label,
            () => button.IsTrackpadGesture() ? gestures : button.HasRelease() ? pressed : fired,
            () => button.IsTrackpadGesture() ? laterGestures : laterButtons,
            () => Settings.ActionsOf(PickerOf(button.FamilyOf()).Layer, button),
            specs => { Settings.SetActions(PickerOf(button.FamilyOf()).Layer, button, specs); _mouse.Apply(); },
            scoped ? scopes.Card($"{Title} › {ButtonsGroupName} › {label}", ButtonsScope, () => Settings.CardScopes.GetValueOrDefault(button.ToString()), scope =>
            {
                Settings.SetCardScope(button, scope);
                _mouse.Apply();
            }) : null, _mouse);

        var buttonCards = Buttons.Select(b => CardFor(b.Button, b.Label, scoped: true)).ToList();
        var gestureCards = TrackpadGestures.All.Select(g => CardFor(g.Button, g.Name, scoped: false)).ToList();
        _buttonItems.AddRange(buttonCards.Concat(gestureCards));

        // The key stays "Mouse/Buttons" so a group the user opened before the rename stays open.
        ButtonsGroup = new ItemGroup("Mouse/Buttons", ButtonsGroupName, buttonCards.Cast<object>().ToList())
        {
            Tip = "A button left at Default works as Windows normally does. The layer drop-down shows the mouse layer in force and changes it (the trackpad "
                  + "has its own); a button set to \"Hold for layer N\" uses layer N while held. Over OmniControl's own window the left and right buttons are never remapped. Windows does "
                  + "not pass mouse input to programs running as administrator unless OmniControl runs as administrator too.",
        };
        TrackpadGroup = BuildTrackpadGroup(gestureCards, report);
        BuildWhereGroup(scopes, hotkeys);
        BuildPointerGroup();

        mouse.LayerChanged += (family, layer) => PickerOf(family).Follow(layer);
        mouse.LayersChanged += () =>
        {
            _mouseLayers.Refresh();
            _trackpadLayers.Refresh();
            ButtonsScope.Refresh();
            ReloadButtons();
        };
        mouse.ButtonPressed += button => Dispatcher.UIThread.Post(() => _buttonItems.FirstOrDefault(i => i.Button == button)?.Flash());

        hotkeys.Register(new HotkeyAction("mouse.layer.next", "Next mouse layer", Title, "mouse layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Mouse)) { CoveredByMousePicker = true });
        hotkeys.Register(new HotkeyAction("mouse.trackpad.layer.next", "Next trackpad layer", Title, "trackpad touchpad gestures layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Trackpad)) { CoveredByMousePicker = true });
        var autoscrollFamily = new OnOffFamily(MouseService.AutoscrollId, "App autoscroll block", MouseService.AutoscrollId);
        var autoscroll = new HotkeyAction(MouseService.AutoscrollId, "Toggle app autoscroll", Title, "middle click autoscroll scroll browser block apps",
            _ => _mouse.ToggleBlockAutoscroll()) { Family = autoscrollFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(autoscrollFamily.Siblings(autoscroll, _mouse.SetBlockAutoscroll).Prepend(autoscroll));
        RegisterPointerActions(hotkeys);
        RegisterClickingAction(hotkeys);
        // Last: a preset group's card is named after the page group it belongs to, and a layer with a saved shortcut gets its action
        // after the page's own actions.
        presets.Register(new PresetGroup(MouseLayersId, Title, ButtonsGroup.Name, new LayerPresets(mouse, InputFamily.Mouse)) { MovesLayer = true });
        presets.Register(new PresetGroup(TrackpadLayersId, Title, TrackpadGroup.Name, new LayerPresets(mouse, InputFamily.Trackpad)) { MovesLayer = true });
    }

    /// <summary>The page was opened: Windows' touchpad and pointer settings may have been changed meanwhile.</summary>
    public override void OnShown()
    {
        _trackpad.Refresh();
        LoadPointerSettings();
    }

    /// <summary>The window came back on screen with this page open: show Windows' pointer settings as they are now.</summary>
    public override void OnOnScreenChanged(bool onScreen)
    {
        if (!onScreen) return;
        LoadPointerSettings();
    }

    private LayerPicker PickerOf(InputFamily family) => family == InputFamily.Trackpad ? _trackpadLayers : _mouseLayers;

    /// <summary>The cogwheel beside the mouse layer drop-down: the preset manager at the Mouse Buttons card.</summary>
    public void ManageMouseLayers() => _openPresets(Title, MouseLayersId);

    /// <summary>The cogwheel beside the trackpad layer drop-down: the preset manager at the Trackpad card.</summary>
    public void ManageTrackpadLayers() => _openPresets(Title, TrackpadLayersId);

    private void ReloadButtons()
    {
        foreach (var item in _buttonItems) item.Reload();
    }

    private void ReloadCards(InputFamily family)
    {
        foreach (var item in _buttonItems.Where(i => i.Button.FamilyOf() == family)) item.Reload();
    }
}
