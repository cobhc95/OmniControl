using Avalonia.VisualTree;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys.Picker;
using OmniControl.App.Features.Input.Pages.Mouse;
using OmniControl.App.Features.Settings.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    private static ActionsWindowService? _actionsWindow;

    /// The Actions and Hotkeys window over the master list every page registered into, its sections the pages in sidebar order. Its keys
    /// are shown, never registered (the master list's hotkey thread was ended at once, <see cref="Rig"/>).
    private static void ActionsWindow(Rig rig, Camera camera)
    {
        rig.Actions.UseSections(rig.Pages.Select((p, i) => new ActionSection(p.Title, p.Icon, i + 1)).ToList());
        var keys = rig.Actions.ExportAll();
        foreach (var (id, key) in new[]
                 {
                     ("displays.brightness.up", "Ctrl+Shift+Up"), ("displays.brightness.down", "Ctrl+Shift+Down"), ("displays.contrast.up", "Ctrl+Shift+PageUp"),
                     ("displays.rotate.portrait", "Ctrl+Alt+Left"), ("displays.rotate.landscape", "Ctrl+Alt+Up"), ("automation.power.lock", "Ctrl+Alt+L"),
                 })
            if (keys.TryGetValue(id, out var saved)) keys[id] = saved with { Gesture = key };
        rig.Actions.ImportAll(keys);
        var service = ActionsService(rig);
        var window = PickerWindow.Manage(ActionsWindowService.Title, service);
        window.Use(new PickerModel(PickChoice.Group(MouseActionCatalog.ManageChoices(rig.Actions.PickerActions)), null, new Dictionary<string, string>(), manage: service.Keys));
        camera.Window(window, "actions-hotkeys");
    }

    private static ActionsWindowService ActionsService(Rig rig) => _actionsWindow ??= new ActionsWindowService(rig.Actions, rig.Presets, Rig.OpenPresets, Rig.Ask, _ => { },
        (_, _) => Task.CompletedTask, HotkeysOn(rig), Rig.Report);

    /// The hotkeys switch as the running "Hotkeys and automation" feature shows it.
    private static HotkeysSwitch HotkeysOn(Rig rig)
    {
        var hotkeys = new HotkeysSwitch(rig.Actions, rig.States, AppSettingsPageViewModel.Name, Rig.Report, AppSettings.Current.Slot(s => s.HotkeysOn, (s, on) => s.HotkeysOn = on));
        hotkeys.Use(true);
        return hotkeys;
    }

    /// The settings of the Forward card's held action (its cogwheel), with the Runs pull-down open; the pull-down's popup is drawn inside
    /// the window's own layer, so the frame has it.
    private static void ActionRunsPulldown(Rig rig, Camera camera)
    {
        ActionsService(rig);
        var card = ForwardCard(MousePage(rig));
        var model = card.Slots[1].MenuFor(true);
        model.Target = card.Label;
        var window = new PickerWindow { Title = $"Choose an action for {card.Label}" };
        window.Use(model);
        camera.Window(window, "action-runs-pulldown", prepare: () =>
        {
            var runs = window.GetVisualDescendants().OfType<ComboBox>().First(b => b.IsEffectivelyVisible);
            runs.IsDropDownOpen = true;
        });
    }

    /// The Choose-the-action window as a mouse button's card opens it (pick mode: "Use this action").
    private static void ActionPicker(Rig rig, Camera camera)
    {
        ActionsService(rig); // gives every picker window the keys part, as at start-up
        var card = _mouse!.ButtonsGroup.Items.OfType<MouseButtonItem>().First(b => b.Label.Contains("Middle", StringComparison.Ordinal));
        var model = card.MenuFor(false);
        model.Target = card.Label;
        var window = new PickerWindow { Title = $"Choose an action for {card.Label}" };
        window.Use(model);
        camera.Window(window, "action-picker");
    }
}
