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
/// The Mouse and Trackpad page: the main switch, the Mouse Buttons group (layer picker on its header), the Trackpad group
/// (<c>MousePageViewModel.Trackpad.cs</c>) and the Pointer settings group (<c>MousePageViewModel.Pointer.cs</c>, its actions in
/// <c>.PointerActions.cs</c>). The first two edit the same layers, mappings and action list; the third changes Windows' own mouse settings.
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
    private readonly List<MouseButtonItem> _buttonItems = new(); // every input's card: mouse buttons, then trackpad gestures
    private int _layer = 1;
    private bool _refreshingMenu;
    private IReadOnlyList<int> _menuLayers = Array.Empty<int>();

    /// <summary>The on/off switch, shown beside the title.</summary>
    public IReadOnlyList<object> Primary { get; }

    /// <summary>The nine button cards, in a collapsible group.</summary>
    public ItemGroup ButtonsGroup { get; }

    /// <summary>The layer picker, shown as a bare drop-down on the Mouse Buttons group's title line; it edits both groups.</summary>
    public ChoiceItem Layer { get; }

    public override IEnumerable<SearchEntry> SearchEntries =>
        Primary.Append(Layer).Select(i => Entry(i, Title, "mouse buttons remap layer " + TrackpadKeywords))
            .Concat(ButtonsGroup.Items.Select(i => Entry(i, Title, "mouse buttons remap layer", ButtonsGroup.Name)))
            .Concat(TrackpadGroup.Items.Select(i => Entry(i, Title, TrackpadKeywords, TrackpadGroup.Name)))
            .Concat(PointerSearchEntries());

    private MouseSettings Settings => _mouse.Settings;

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

        Layer = new ChoiceItem("Layer", new[] { string.Empty }, string.Empty, OnLayerPicked);
        _layer = mouse.Layer;
        RefreshLayerMenu();

        // The action list follows the master list: an action a feature registers later appears on every card's list. Inputs without a
        // release (wheel, tilt, gestures) get the list with the "while held" actions greyed.
        List<PickChoice> pressed = MouseActionCatalog.Choices(hotkeys.Actions), fired = MouseActionCatalog.Choices(hotkeys.Actions, hasRelease: false);
        hotkeys.Actions.CollectionChanged += (_, _) =>
        {
            pressed = MouseActionCatalog.Choices(hotkeys.Actions);
            fired = MouseActionCatalog.Choices(hotkeys.Actions, hasRelease: false);
            ReloadButtons();
        };
        MouseButtonItem CardFor(MouseButton button, string label) => new(button, label, () => button.HasRelease() ? pressed : fired,
            () => Settings.Get(_layer, button) ?? MouseActionCatalog.Default,
            spec => { Settings.Set(_layer, button, spec); _mouse.Apply(); });

        var buttonCards = Buttons.Select(b => CardFor(b.Button, b.Label)).ToList();
        var gestureCards = TrackpadGestures.All.Select(g => CardFor(g.Button, g.Name)).ToList();
        _buttonItems.AddRange(buttonCards.Concat(gestureCards));

        Primary = new object[] { _enabled };
        // The key stays "Mouse/Buttons" so a group the user opened before the rename stays open.
        ButtonsGroup = new ItemGroup("Mouse/Buttons", "Mouse Buttons", buttonCards.Cast<object>().ToList())
        {
            Tip = "A button left at Default works as Windows normally does. The Layer drop-down shows the layer in force and changes it; a button set to "
                  + "\"Hold for layer N\" uses layer N while held. Over OmniControl's own window the left and right buttons are never remapped. Windows does "
                  + "not pass mouse input to programs running as administrator unless OmniControl runs as administrator too.",
        };
        TrackpadGroup = BuildTrackpadGroup(gestureCards, report);
        BuildPointerGroup();

        mouse.StateChanged += SyncFromService;
        mouse.LayerChanged += layer =>
        {
            // The drop-down follows the layer in force, so it doubles as a live indicator (without telling the service back).
            _layer = layer;
            RefreshLayerMenu();
            ReloadButtons();
        };
        mouse.LayersChanged += () =>
        {
            RefreshLayerMenu();
            ReloadButtons();
        };
        mouse.ButtonPressed += button => Dispatcher.UIThread.Post(() => _buttonItems.FirstOrDefault(i => i.Button == button)?.Flash());

        hotkeys.Register(new HotkeyAction("mouse.toggle", "Toggle mouse remapping", Title, "mouse buttons remap on off",
            _ => _enabled.IsOn = !_enabled.IsOn));
        hotkeys.Register(new HotkeyAction("mouse.layer.next", "Next mouse layer", Title, "mouse layer switch next",
            _ => mouse.StepLayer()));
        hotkeys.Register(new HotkeyAction("mouse.trackpad.toggle", "Toggle trackpad gestures", Title, "trackpad touchpad gestures swipe pinch on off",
            _ => _trackpadOn.IsOn = !_trackpadOn.IsOn));
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
        if (onScreen) LoadPointerSettings();
    }

    /// <summary>The layer list and the name shown, from the settings and the layer being shown.</summary>
    private void RefreshLayerMenu()
    {
        var labels = Settings.LayerLabels();
        _menuLayers = Settings.ListedLayers(_layer, _mouse.Layer);
        _refreshingMenu = true;
        Layer.Options = _menuLayers.Select(n => labels[n]).ToList();
        Layer.Selected = labels[_layer];
        _refreshingMenu = false;
    }

    /// <summary>A layer picked in the drop-down becomes the layer being edited and the layer in force.</summary>
    private void OnLayerPicked(string? text)
    {
        if (_refreshingMenu || text is null) return;
        var index = Layer.Options.ToList().IndexOf(text);
        if (index < 0 || index >= _menuLayers.Count) return;
        _layer = _menuLayers[index];
        ReloadButtons();
        _mouse.SetLayer(_layer);
    }

    /// <summary>The model of the window that renames and clears layers.</summary>
    public LayerManagerModel CreateLayerManager() => new(_mouse);

    private void ReloadButtons()
    {
        foreach (var item in _buttonItems) item.Reload();
    }

    /// <summary>A hotkey changed the on/off state: show it.</summary>
    private void SyncFromService()
    {
        if (_enabled.IsOn != Settings.Enabled) _enabled.IsOn = Settings.Enabled;
    }
}
