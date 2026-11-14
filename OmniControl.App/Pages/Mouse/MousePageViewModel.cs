using Avalonia.Threading;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Picker;
using OmniControl.App.Osd;
using OmniControl.Core.Engine.Mouse;
using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.App.Pages.Mouse;

/// <summary>
/// The Mouse and Trackpad page: the main switch, the Mouse Buttons group (mouse layer picker on its header), the Trackpad group
/// (<c>MousePageViewModel.Trackpad.cs</c>, trackpad layer picker on its header) and the Pointer settings group
/// (<c>MousePageViewModel.Pointer.cs</c>, its actions in <c>.PointerActions.cs</c>). The first two share the action list and the mappings
/// file, each with its own layers (<see cref="InputFamily"/>); the third changes Windows' own mouse settings.
/// </summary>
public sealed partial class MousePageViewModel : PageViewModel
{
    public override string Title => "Mouse and Trackpad";

    public override string KeyHelp => string.Join(Environment.NewLine,
        "A pair of half-width cards reads left, then right. Enter on an action (or its cogwheel) opens the action picker.",
        "Action picker: " + PickerModel.KeyHelp);
    public override bool ShowsOwnHeader => true; // the on/off switch sits on the title line
    public override string Icon => "M12,2 C8.13,2 5,5.13 5,9 V15 C5,18.87 8.13,22 12,22 C15.87,22 19,18.87 19,15 V9 C19,5.13 15.87,2 12,2 Z M11,5 H13 V10 H11 Z M5,11.5 H19 V13 H5 Z";
    public override string Keywords => "mouse buttons remap layer wheel tilt x-mouse button control side button middle click " + TrackpadKeywords + " " + PointerKeywords;

    private static readonly (MouseButton Button, string Label)[] Buttons =
    {
        (MouseButton.Left, "Left button"), (MouseButton.Right, "Right button"), (MouseButton.Middle, "Middle button"),
        (MouseButton.Back, "Back (button 4)"), (MouseButton.Forward, "Forward (button 5)"),
        (MouseButton.WheelUp, "Wheel up"), (MouseButton.WheelDown, "Wheel down"),
        (MouseButton.TiltLeft, "Tilt left"), (MouseButton.TiltRight, "Tilt right"),
    };

    private readonly MouseService _mouse;
    private readonly ToggleItem _enabled;
    private const string ButtonKeywords = "mouse buttons remap layer preset";

    private readonly List<MouseButtonItem> _buttonItems = new(); // every input's card: mouse buttons, then trackpad gestures
    private readonly LayerPicker _mouseLayers;
    private readonly LayerPicker _trackpadLayers;

    /// <summary>The on/off switch, shown beside the title.</summary>
    public IReadOnlyList<object> Primary { get; }

    /// <summary>The nine button cards, in a collapsible group.</summary>
    public ItemGroup ButtonsGroup { get; }

    /// <summary>The mouse layer picker, shown as a bare drop-down on the Mouse Buttons group's title line; it edits the button cards only.</summary>
    public ChoiceItem MouseLayer => _mouseLayers.Choice;

    /// <summary>The trackpad layer picker, the same drop-down on the Trackpad group's title line; it edits the gesture cards only.</summary>
    public ChoiceItem TrackpadLayer => _trackpadLayers.Choice;

    public override IEnumerable<SearchEntry> SearchEntries =>
        Primary.Select(i => Entry(i, Title, ButtonKeywords + " " + TrackpadKeywords))
            .Append(Entry(MouseLayer, Title, ButtonKeywords, ButtonsGroup.Name))
            .Concat(ButtonsGroup.Items.Select(i => Entry(i, Title, WithActionNames(i, ButtonKeywords), ButtonsGroup.Name)))
            .Append(Entry(TrackpadLayer, Title, TrackpadKeywords, TrackpadGroup.Name))
            .Concat(TrackpadGroup.Items.Select(i => Entry(i, Title, WithActionNames(i, TrackpadKeywords), TrackpadGroup.Name)))
            .Concat(PointerSearchEntries());

    private MouseSettings Settings => _mouse.Settings;

    // A card is also found by the names of its actions (search reads this each time it runs, so it is always current).
    private static string WithActionNames(object item, string keywords) => item is MouseButtonItem card ? $"{keywords} {card.SearchWords}" : keywords;

    /// <param name="ask">Shows a short message with a confirm button (the button-swap "Keep" question).</param>
    public MousePageViewModel(MouseService mouse, TrackpadService trackpad, PointerSettingsService pointer, HotkeyRegistry hotkeys, OsdService osd,
        Func<EngineMessage, Task<bool>> ask, Action<string> report)
    {
        _mouse = mouse;
        _trackpad = trackpad;
        _pointer = pointer;
        _osd = osd;
        _ask = ask;
        _report = report;

        _enabled = new ToggleItem("Remap mouse and trackpad", Settings.Enabled, mouse.SetEnabled) { Half = true };

        _mouseLayers = new LayerPicker(mouse, InputFamily.Mouse, "Mouse layer", () => ReloadCards(InputFamily.Mouse));
        _trackpadLayers = new LayerPicker(mouse, InputFamily.Trackpad, "Trackpad layer", () => ReloadCards(InputFamily.Trackpad));

        // The action list follows the master list: an action a feature registers later appears on every card's list. Inputs without a
        // release (wheel, tilt, gestures) get the list with the "while held" actions greyed; a trackpad gesture's settings word "keep the
        // original" as Windows' own gesture.
        // The second and later actions of a card run once each (one-shot lists, MouseActionCatalog.ExtraChoices).
        List<PickChoice> pressed = new(), fired = new(), gestures = new(), laterButtons = new(), laterGestures = new();
        void BuildLists()
        {
            pressed = MouseActionCatalog.Choices(hotkeys.Actions);
            fired = MouseActionCatalog.Choices(hotkeys.Actions, hasRelease: false);
            gestures = MouseActionCatalog.Choices(hotkeys.Actions, hasRelease: false, gesture: true);
            laterButtons = MouseActionCatalog.ExtraChoices(hotkeys.Actions);
            laterGestures = MouseActionCatalog.ExtraChoices(hotkeys.Actions, gesture: true);
        }
        BuildLists();
        hotkeys.Actions.CollectionChanged += (_, _) =>
        {
            BuildLists();
            ReloadButtons();
        };
        MouseButtonItem CardFor(MouseButton button, string label) => new(button, label,
            () => button.IsTrackpadGesture() ? gestures : button.HasRelease() ? pressed : fired,
            () => button.IsTrackpadGesture() ? laterGestures : laterButtons,
            () => Settings.ActionsOf(PickerOf(button.FamilyOf()).Layer, button),
            specs => { Settings.SetActions(PickerOf(button.FamilyOf()).Layer, button, specs); _mouse.Apply(); });

        var buttonCards = Buttons.Select(b => CardFor(b.Button, b.Label)).ToList();
        var gestureCards = TrackpadGestures.All.Select(g => CardFor(g.Button, g.Name)).ToList();
        _buttonItems.AddRange(buttonCards.Concat(gestureCards));

        Primary = new object[] { _enabled };
        // The key stays "Mouse/Buttons" so a group the user opened before the rename stays open.
        ButtonsGroup = new ItemGroup("Mouse/Buttons", "Mouse Buttons", buttonCards.Cast<object>().ToList())
        {
            Tip = "A button left at Default works as Windows normally does. The layer drop-down shows the mouse layer in force and changes it (the trackpad "
                  + "has its own); a button set to \"Hold for layer N\" uses layer N while held. Over OmniControl's own window the left and right buttons are never remapped. Windows does "
                  + "not pass mouse input to programs running as administrator unless OmniControl runs as administrator too.",
        };
        TrackpadGroup = BuildTrackpadGroup(gestureCards, report);
        BuildPointerGroup();

        mouse.StateChanged += SyncFromService;
        mouse.LayerChanged += (family, layer) => PickerOf(family).Follow(layer);
        mouse.LayersChanged += () =>
        {
            _mouseLayers.Refresh();
            _trackpadLayers.Refresh();
            ReloadButtons();
        };
        mouse.ButtonPressed += button => Dispatcher.UIThread.Post(() => _buttonItems.FirstOrDefault(i => i.Button == button)?.Flash());

        hotkeys.Register(new HotkeyAction("mouse.toggle", "Toggle mouse remapping", Title, "mouse buttons remap on off",
            _ => _enabled.IsOn = !_enabled.IsOn));
        hotkeys.Register(new HotkeyAction("mouse.layer.next", "Next mouse layer", Title, "mouse layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Mouse)));
        hotkeys.Register(new HotkeyAction("mouse.trackpad.layer.next", "Next trackpad layer", Title, "trackpad touchpad gestures layer preset switch next",
            _ => mouse.StepLayer(InputFamily.Trackpad)));
        RegisterPointerActions(hotkeys);
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

    /// <summary>The model of the window that renames and clears the mouse's layers.</summary>
    public LayerManagerModel CreateMouseLayerManager() => new(_mouse, InputFamily.Mouse);

    /// <summary>The model of the window that renames and clears the trackpad's layers.</summary>
    public LayerManagerModel CreateTrackpadLayerManager() => new(_mouse, InputFamily.Trackpad);

    private void ReloadButtons()
    {
        foreach (var item in _buttonItems) item.Reload();
    }

    private void ReloadCards(InputFamily family)
    {
        foreach (var item in _buttonItems.Where(i => i.Button.FamilyOf() == family)) item.Reload();
    }

    /// <summary>A hotkey changed the on/off state: show it.</summary>
    private void SyncFromService()
    {
        if (_enabled.IsOn != Settings.Enabled) _enabled.IsOn = Settings.Enabled;
    }
}
