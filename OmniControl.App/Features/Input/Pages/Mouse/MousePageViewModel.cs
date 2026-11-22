using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Input.Pages.Gestures;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The Mouse and Trackpad page, everything mouse-driven: the Mouse Buttons group (mouse layer picker on its header), the Trackpad group
/// (<c>MousePageViewModel.Trackpad.cs</c>, trackpad layer picker on its header), the gesture groups and custom gesture rules
/// (<see cref="GestureGroups"/>), "All mouse and trackpad features" (<c>.Where.cs</c>) and the Pointer settings group
/// (<c>MousePageViewModel.Pointer.cs</c>, its actions in <c>.PointerActions.cs</c>). Buttons and trackpad share the action list and the
/// mappings file, each with its own layers (<see cref="InputFamily"/>); the pointer settings change Windows' own mouse settings.
public sealed partial class MousePageViewModel : PageViewModel, IDisposable
{
    public static readonly PageInfo Info = new(ActionSections.Mouse,
        "M12,2 C8.13,2 5,5.13 5,9 V15 C5,18.87 8.13,22 12,22 C15.87,22 19,18.87 19,15 V9 C19,5.13 15.87,2 12,2 Z M11,5 H13 V10 H11 Z M5,11.5 H19 V13 H5 Z", NavGroups.Devices,
        "mouse buttons remap layer wheel tilt x-mouse button control side button middle click " + TrackpadKeywords + " " + GestureKeywords
        + " " + PointerActions.Keywords + " " + WhereKeywords, "mouse");

    public override string KeyHelp => string.Join(Environment.NewLine,
        "A pair of half-width cards reads left, then right. Enter on an action (or its cogwheel) opens the action picker.",
        GestureGroups.KeyHelp,
        "Rule editor: " + RuleEditorViewModel.KeyHelp,
        "Action picker: " + PickerModel.KeyHelp);

    private const string GestureKeywords = "gestures wheel scroll taskbar edge title bar desktop hold key volume brightness opacity zoom custom rules";

    /// The gesture groups and custom gesture rules, between the Trackpad group and "All mouse and trackpad features".
    public GestureGroups Gestures { get; }

    private static readonly (MouseButton Button, string Label)[] Buttons =
    {
        (MouseButton.Left, "Left button"), (MouseButton.Right, "Right button"), (MouseButton.Middle, "Middle button"),
        (MouseButton.Back, "Back (button 4)"), (MouseButton.Forward, "Forward (button 5)"),
        (MouseButton.WheelUp, "Wheel up"), (MouseButton.WheelDown, "Wheel down"),
        (MouseButton.TiltLeft, "Tilt left"), (MouseButton.TiltRight, "Tilt right"),
    };

    private readonly MouseService _mouse;
    private readonly IActionRegistry _hotkeys;
    private const string ButtonKeywords = "mouse buttons remap layer preset apps scope";
    /// The Mouse Buttons group's name, also its layer preset group's card (<see cref="InputActions"/>).
    public const string ButtonsGroupName = "Mouse Buttons";

    private readonly List<MouseButtonItem> _buttonItems = new(); // every input's card: mouse buttons, then trackpad gestures
    private readonly LayerPicker _mouseLayers;
    private readonly LayerPicker _trackpadLayers;

    /// The nine button cards, in a collapsible group.
    public ItemGroup ButtonsGroup { get; }

    /// The apps the Mouse Buttons group works in: the "Apps: ..." chip on its title line, left of the layer picker.
    public AppScopeModel ButtonsScope { get; }

    /// The mouse layer picker, shown as a bare drop-down on the Mouse Buttons group's title line; it edits the button cards only.
    public ChoiceItem MouseLayer => _mouseLayers.Choice;

    /// The trackpad layer picker, the same drop-down on the Trackpad group's title line; it edits the gesture cards only.
    public ChoiceItem TrackpadLayer => _trackpadLayers.Choice;

    private MouseSettings Settings => _mouse.Settings;

    public const string MouseLayersId = LayerPresets.MouseGroupId, TrackpadLayersId = LayerPresets.TrackpadGroupId;

    private readonly Action<string?, string?> _openPresets;

    /// <param name="hotkeys">The master action list, whose actions the cards offer (they follow its changes).</param>
    /// <param name="pointerActions">The pointer settings' actions and the steps the Pointer settings cards share with them.</param>
    /// <param name="openPresets">Opens the preset manager at a section and group (the header cogwheels).</param>
    /// <param name="scopes">Makes the apps scope models of the Mouse Buttons and Trackpad groups and their cards.</param>
    /// <param name="gestureGroups">The gesture groups and custom gesture rules this page shows.</param>
    public MousePageViewModel(MouseService mouse, TrackpadService trackpad, PointerSettingsService pointer, PointerActions pointerActions, IActionRegistry hotkeys,
        Action<string> report, Action<string?, string?> openPresets, AppScopes scopes, GestureGroups gestureGroups) : base(Info)
    {
        Gestures = gestureGroups;
        _hotkeys = hotkeys;
        _openPresets = openPresets;
        _mouse = mouse;
        _trackpad = trackpad;
        _pointer = pointer;
        _pointerActions = pointerActions;
        _report = report;

        _mouseLayers = new LayerPicker(mouse, InputFamily.Mouse, MouseLayerLabel, () => ReloadCards(InputFamily.Mouse), ManageMouseLayers);
        _trackpadLayers = new LayerPicker(mouse, InputFamily.Trackpad, TrackpadLayerLabel, () => ReloadCards(InputFamily.Trackpad), ManageTrackpadLayers);

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
        _followed.Follow<System.Collections.Specialized.NotifyCollectionChangedEventHandler>(h => hotkeys.Actions.CollectionChanged += h, h => hotkeys.Actions.CollectionChanged -= h, (_, _) =>
        {
            BuildLists();
            ReloadButtons();
        });
        ButtonsScope = scopes.Group($"{Title} › {ButtonsGroupName}", () => Settings.ButtonsScope, scope =>
        {
            Settings.ButtonsScope = scope;
            _mouse.Apply();
        });
        TrackpadScope = scopes.Group($"{Title} › {TrackpadGroupName}", () => Settings.TrackpadScope, scope =>
        {
            Settings.TrackpadScope = scope;
            _mouse.Apply();
        });
        MouseButtonItem CardFor(MouseButton button, string label, AppScopeModel group, string groupName) => new(button, label,
            () => button.IsTrackpadGesture() ? gestures : button.HasRelease() ? pressed : fired,
            () => button.IsTrackpadGesture() ? laterGestures : laterButtons,
            () => Settings.ActionsOf(PickerOf(button.FamilyOf()).Layer, button),
            specs => _mouse.SetActions(PickerOf(button.FamilyOf()).Layer, button, specs),
            scopes.Card($"{Title} › {groupName} › {label}", group, () => Settings.CardScopes.GetValueOrDefault(button.ToString()), scope =>
            {
                Settings.SetCardScope(button, scope);
                _mouse.Apply();
            }), _mouse);

        var buttonCards = Buttons.Select(b => CardFor(b.Button, b.Label, ButtonsScope, ButtonsGroupName)).ToList();
        var gestureCards = TrackpadGestures.All.Select(g => CardFor(g.Button, g.Name, TrackpadScope, TrackpadGroupName)).ToList();
        _buttonItems.AddRange(buttonCards.Concat(gestureCards));

        // The key stays "Mouse/Buttons" so a group the user opened before the rename stays open.
        ButtonsGroup = new ItemGroup("Mouse/Buttons", ButtonsGroupName, buttonCards.Cast<object>().ToList())
        {
            Tip = ButtonsTip,
        };
        TrackpadGroup = BuildTrackpadGroup(gestureCards, report);
        BuildWhereGroup(scopes);
        BuildPointerGroup();

        _followed.Follow<Action<InputFamily, int>>(h => mouse.LayerChanged += h, h => mouse.LayerChanged -= h, (family, layer) =>
        {
            PickerOf(family).Follow(layer);
            ShowMouseLayer();
        });
        _followed.Follow<Action>(h => mouse.LayersChanged += h, h => mouse.LayersChanged -= h, () =>
        {
            _mouseLayers.Refresh();
            _trackpadLayers.Refresh();
            ButtonsScope.Refresh();
            TrackpadScope.Refresh();
            ReloadButtons();
            ShowMouseLayer();
        });
        ShowMouseLayer();
        _followed.Follow<Action>(h => mouse.Applied += h, h => mouse.Applied -= h, Gestures.RefreshRules); // a changed mapping can start or end a custom rule's conflict
        _followed.Follow<Action<MouseButton>>(h => mouse.ButtonPressed += h, h => mouse.ButtonPressed -= h,
            button => Dispatcher.UIThread.Post(() => _buttonItems.FirstOrDefault(i => i.Button == button)?.Flash()));
    }

    /// The page was opened: Windows' touchpad and pointer settings may have been changed meanwhile.
    // The pointer settings are read when the page comes on screen (OnOnScreenChanged), which follows at once.
    public override void OnShown() => _trackpad.Refresh();

    /// The window came back on screen with this page open: show Windows' pointer settings as they are now. While the page is on screen
    /// every press and gesture is wanted, so its card can flash.
    public override void OnOnScreenChanged(bool onScreen)
    {
        Gestures.OnOnScreenChanged(onScreen);
        if (!onScreen)
        {
            _pressWatch?.Dispose();
            _pressWatch = null;
            return;
        }
        _pressWatch ??= _mouse.WatchPresses();
        LoadPointerSettings();
    }

    private IDisposable? _pressWatch;
    private readonly Subscriptions _followed = new(); // the services' events this page follows, given back at Dispose

    public void Dispose()
    {
        _followed.Dispose();
        Gestures.Dispose();
        _pressWatch?.Dispose();
    }

    private LayerPicker PickerOf(InputFamily family) => family == InputFamily.Trackpad ? _trackpadLayers : _mouseLayers;

    // The trackpad's layer in force is shown by its own drop-down on this page; the footer keeps one short fact.
    private void ShowMouseLayer() => FooterStatus = $"Mouse layer: {_mouse.Settings.LayersOf(InputFamily.Mouse).Labels()[_mouse.LayerOf(InputFamily.Mouse)]}";

    /// The cogwheel beside the mouse layer drop-down: the preset manager at the Mouse Buttons card.
    public void ManageMouseLayers() => _openPresets(Title, MouseLayersId);

    /// The cogwheel beside the trackpad layer drop-down: the preset manager at the Trackpad card.
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
