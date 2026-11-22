using System.Collections.ObjectModel;
using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Volume.Fx;
using OmniControl.App.Features.Volume.Fx.Presets;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Displays.Pages;
using OmniControl.App.Shared.Presets.Manager;
using OmniControl.App.Shared.Presets;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Tests;

/// The unified preset manager, headless and with fakes (no shortcut is registered on the PC, nothing is written under the user's
/// folder): each group's adapter (layers, picture and monitor presets, an effect kind's presets), the stable ids and order that are
/// saved, the preset shortcuts in the master list of actions, where the manager opens, the dimmed sections, and building every piece in
/// the shell's order with no data at all.
public class PresetManagerTests
{
    // ---------------------------------------------------------------- fakes

    /// The master list of actions in memory: what hotkeys.json would keep is <see cref="Saved"/>.
    private sealed class FakeActions : IActionRegistry
    {
        public ObservableCollection<HotkeyAction> Actions { get; } = new();
        public Dictionary<string, string> Saved { get; } = new();
        public IEnumerable<HotkeyAction> PickerActions => Actions;
        public event Action? BindingsSaved { add { } remove { } }
        public event Action<IReadOnlyDictionary<string, string>>? Moved { add { } remove { } }

        public void Register(HotkeyAction action)
        {
            if (Saved.TryGetValue(action.Id, out var gesture)) action.Restore(new StoredHotkey(gesture, false, new List<string>()));
            action.Changed += a => { if (Actions.Contains(a)) Saved[a.Id] = a.Gesture; };
            Actions.Add(action);
        }

        public void Register(IEnumerable<HotkeyAction> actions)
        {
            foreach (var action in actions) Register(action);
        }

        public void RegisterPickerCommand(HotkeyAction command) { }

        public void Move(IReadOnlyDictionary<string, string> moved) { }

        public void Unregister(string id)
        {
            foreach (var gone in Actions.Where(a => a.Id == id).ToList()) Actions.Remove(gone);
        }

        public HotkeyAction? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

        public ActionSection? SectionOf(string section) => null;

        public HotkeyAction? OwnerOf(string gesture, string exceptId) => HotkeyRegistry.OwnerOf(gesture, exceptId, Actions);

        public IReadOnlyList<string> SavedWithShortcut(string prefix) =>
            Saved.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Value.Length > 0).Select(p => p.Key).ToList();

        public string? ProblemWith(string gesture, string id) => HotkeyRegistry.ConflictOf(gesture, id, Actions);
    }

    /// The mouse service's layer side, in memory (MouseSettings never saved).
    private sealed class FakeLayers : ILayerControl
    {
        private readonly Dictionary<InputFamily, int> _current = new() { [InputFamily.Mouse] = 1, [InputFamily.Trackpad] = 1 };

        public MouseSettings Settings { get; } = MouseSettings.Defaults();
        public int LayerOf(InputFamily family) => _current[family];

        public void SetLayer(InputFamily family, int layer)
        {
            _current[family] = layer;
            LayerChanged?.Invoke(family, layer);
        }

        public void RenameLayer(InputFamily family, int layer, string name) => Change(() => Settings.LayersOf(family).SetName(layer, name));
        public void ClearLayer(InputFamily family, int layer) => Change(() => Settings.LayersOf(family).Clear(layer));
        public void RestoreLayer(InputFamily family, LayerSnapshot snapshot) => Change(() => Settings.LayersOf(family).Restore(snapshot));
        public void SetOnlyUsedLayers(InputFamily family, bool on) => Change(() => Settings.LayersOf(family).OnlyUsed = on);

        private void Change(Action change)
        {
            change();
            LayersChanged?.Invoke();
        }

        public event Action<InputFamily, int>? LayerChanged;
        public event Action? LayersChanged;
    }

    /// An effect the presets are read from and applied to.
    private sealed class FakeTarget : IFxPresetTarget
    {
        public PresetContent Content { get; set; } = new() { Bands = Eq.Layout.Default() };
        public PresetContent Read() => Content.Copy();
        public void Apply(PresetContent content) => Content = content.Copy();
    }

    private static FxKind EqKind => FxCatalog.Find("eq");

    private static UserPresetStore PresetStore(List<UserPresetStore.Stored>? saves = null) => new(null, s => saves?.Add(s));

    private static PresetContent Tuned()
    {
        var bands = Eq.Layout.Default();
        bands[0].Gain = 7;
        return new PresetContent { Bands = bands };
    }

    // ---------------------------------------------------------------- layers

    [Fact]
    public void Layers_ListWhatTheDropDownLists_SwitchAndRename()
    {
        var mouse = new FakeLayers();
        using var trackpad = new LayerPresets(mouse, InputFamily.Trackpad);
        Assert.Equal(new[] { "Trackpad 1", "Trackpad 2", "Trackpad 3" }, trackpad.Entries.Select(e => e.Name));
        Assert.True(trackpad.Entries[0].InUse);
        Assert.All(trackpad.Entries, e => Assert.False(e.CanMove));

        trackpad.Apply("2");
        Assert.Equal(2, mouse.LayerOf(InputFamily.Trackpad));
        Assert.True(trackpad.Entries[1].InUse);

        Assert.Null(trackpad.Rename("2", "Browsing").Problem);
        Assert.Equal("Browsing", trackpad.Entries[1].Name);
        Assert.NotNull(trackpad.Rename("2", "a/b").Problem);
        Assert.NotNull(trackpad.Move("2", 0).Problem); // layers keep their numbers
    }

    [Fact]
    public void Layers_AddNamesTheNextFreeLayer_DeleteClearsAndForgets_AndUndoPutsItBack()
    {
        var mouse = new FakeLayers();
        using var layers = new LayerPresets(mouse, InputFamily.Mouse);
        Assert.Single(layers.Entries); // only layer 1 is always listed

        var made = layers.Add();
        Assert.Equal("2", made.Id);
        Assert.Equal(new[] { "Layer 1", "Layer 2" }, layers.Entries.Select(e => e.Name));
        mouse.Settings.Set(2, MouseButton.Middle, MouseActionCatalog.Disabled);
        Assert.Equal("1 mapping", layers.Entries[1].Tags.Single().Text);

        var undo = layers.Delete("2");
        Assert.Single(layers.Entries);
        Assert.Null(mouse.Settings.Get(2, MouseButton.Middle));
        undo!();
        Assert.Equal(2, layers.Entries.Count);
        Assert.Equal(MouseActionCatalog.Disabled, mouse.Settings.Get(2, MouseButton.Middle));
    }

    [Fact]
    public void Layers_AnAlwaysListedLayer_IsOnlyCleared_AndTheSwitchShowsEveryLayer()
    {
        var mouse = new FakeLayers();
        using var layers = new LayerPresets(mouse, InputFamily.Mouse);
        Assert.True(layers.Entries[0].ClearsOnly);
        Assert.False(layers.Entries[0].CanDelete); // nothing to clear yet
        Assert.Null(layers.Delete("1"));

        mouse.Settings.Set(1, MouseButton.Back, MouseActionCatalog.Disabled);
        Assert.NotNull(layers.Delete("1"));
        Assert.Single(layers.Entries);

        layers.Switch!.Set(false);
        Assert.Equal(MouseSettings.Layers, layers.Entries.Count);
        Assert.NotNull(layers.AddUnavailableReason);
        Assert.NotNull(layers.Add().Problem);
    }

    // ---------------------------------------------------------------- display presets

    [Fact]
    public void PicturePresets_AddApplyRenameMoveDelete_AndTheOrderIsSaved()
    {
        var saves = new List<DisplaySettings>();
        var store = new DisplaySettingsStore(null, saves.Add);
        DisplayPreset? applied = null;
        var shown = new DisplayPreset { Saturation = 80 };
        var book = new PicturePresetBook(store, () => shown, p => applied = p);
        var card = new BookPresets(book);

        var first = card.Add();
        shown = new DisplayPreset { Saturation = 50 };
        var second = card.Add();
        Assert.Equal(new[] { "Picture", "Picture 2" }, card.Entries.Select(e => e.Name));
        Assert.True(card.Entries[1].InUse);

        Assert.Null(card.Apply(first.Id!));
        Assert.Equal(80, applied!.Saturation);

        Assert.NotNull(card.Rename(second.Id!, "picture").Problem); // taken (any case)
        Assert.Null(card.Rename(second.Id!, "Evening").Problem);
        card.Move(second.Id!, 0);
        Assert.Equal(new[] { "Evening", "Picture" }, store.Data.Presets.Select(p => p.Name));
        Assert.Equal(second.Id, store.Data.Presets[0].Id); // the id stays through rename and move

        var undo = card.Delete(first.Id!);
        Assert.Single(card.Entries);
        undo!();
        Assert.Equal(new[] { "Evening", "Picture" }, card.Entries.Select(e => e.Name));
        Assert.NotEmpty(saves);
    }

    [Fact]
    public void PresetIds_AreGivenToAnOlderFileOnce_AndKeptWhenAPresetIsReplaced()
    {
        var saves = 0;
        var data = new DisplaySettings { Presets = { new DisplayPreset { Name = "Old" } } };
        var store = new DisplaySettingsStore(data, _ => saves++);
        var id = store.Presets.Single().Id;
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(1, saves); // saved once, so the id stays the same next time

        store.Save(new DisplayPreset { Name = "old", Saturation = 10 });
        Assert.Equal(id, store.Presets.Single().Id);
    }

    [Fact]
    public void MonitorPresets_WithNoMonitor_CannotBeAdded_AndSayWhy()
    {
        var store = new DisplaySettingsStore(null, _ => { });
        using var book = new MonitorPresetBook(store, () => Array.Empty<IMonitorPresetTarget>());
        var card = new BookPresets(book);
        Assert.Equal(MonitorPresetBook.NoControls, card.AddUnavailableReason);
        Assert.Equal(MonitorPresetBook.NoControls, card.Add().Problem);
        Assert.Empty(card.Entries);
    }

    // ---------------------------------------------------------------- effect presets

    [Fact]
    public void EffectPresets_ListBuiltInsFirst_AndSaveRenameMoveDuplicateDelete()
    {
        var target = new FakeTarget { Content = Tuned() };
        var store = PresetStore();
        using var presets = new FxKindPresets(EqKind, store, _ => target);
        var builtIns = PresetCatalog.BuiltIns(EqKind).Count;
        Assert.Equal(builtIns, presets.Entries.Count);
        Assert.All(presets.Entries, e => Assert.True(e.BuiltIn && !e.CanRename && !e.CanDelete && !e.CanMove));
        Assert.DoesNotContain(presets.Entries, e => e.InUse); // tuned by hand: Custom

        var mine = presets.Add();
        Assert.Equal("My preset", presets.Find(mine.Id!)!.Name);
        Assert.True(presets.Find(mine.Id!)!.InUse);

        Assert.Equal("That name belongs to a built-in preset.", presets.Rename(mine.Id!, Eq.FlatName).Problem);
        Assert.Null(presets.Rename(mine.Id!, "Desk").Problem);

        var copy = presets.Duplicate("builtin.Bass boost");
        Assert.Equal("Bass boost copy", presets.Find(copy.Id!)!.Name);
        Assert.Null(presets.Move(copy.Id!, builtIns).Problem); // first of the user's own
        Assert.Equal(new[] { "Bass boost copy", "Desk" }, store.Names(EqKind.Kind));

        var undo = presets.Delete(mine.Id!);
        Assert.Equal(new[] { "Bass boost copy" }, store.Names(EqKind.Kind));
        undo!();
        Assert.Equal(mine.Id, store.Find(EqKind.Kind, "Desk")!.Id);
    }

    [Fact]
    public void EffectPresets_ApplyToTheTarget_OrSayThereIsNone()
    {
        var target = new FakeTarget();
        using var presets = new FxKindPresets(EqKind, PresetStore(), _ => target);
        Assert.Null(presets.Apply("builtin.Bass boost"));
        Assert.True(presets.Find("builtin.Bass boost")!.InUse);

        using var none = new FxKindPresets(EqKind, PresetStore(), _ => null);
        Assert.NotNull(none.Apply("builtin.Bass boost"));
        Assert.NotNull(none.Add().Problem);
    }

    [Fact]
    public void EffectPresets_ExportAndImport_RoundTrip_WithoutTheId()
    {
        var store = PresetStore();
        using var presets = new FxKindPresets(EqKind, store, _ => new FakeTarget { Content = Tuned() });
        var mine = presets.Add();
        var bytes = presets.Export(mine.Id!)!;
        Assert.False(System.Text.Json.JsonDocument.Parse(bytes).RootElement.TryGetProperty("Id", out _)); // the bands keep theirs

        var imported = presets.Import("Desk.json", bytes);
        Assert.Equal("Desk", presets.Find(imported.Id!)!.Name);
        Assert.NotEqual(mine.Id, imported.Id);
        Assert.NotNull(new FxKindPresets(FxCatalog.Find(FxCatalog.CompKind), store, _ => null).Import("Desk.json", bytes).Problem);
    }

    [Fact]
    public void AnOlderPresetsFile_GetsIdsOnce()
    {
        var saves = new List<UserPresetStore.Stored>();
        var old = new UserPresetStore.Stored { Presets = { new UserPreset { Kind = "eq", Name = "Old", Content = new PresetContent { Bands = Eq.Layout.Default() } } } };
        var store = new UserPresetStore(old, saves.Add);
        Assert.False(string.IsNullOrEmpty(store.Of("eq").Single().Id));
        Assert.Single(saves);
    }

    [Fact]
    public void WritingAPresetIntoAnEffectWithNoEditor_KeepsHandTunedSettingsAsCustom()
    {
        var slot = FxCatalog.Create("eq");
        slot.Bands = Tuned().Bands;
        var bass = PresetCatalog.BuiltIns(EqKind).First(b => b.Name == "Bass boost").Content;
        PresetCatalog.Write(EqKind, slot, bass, Array.Empty<UserPreset>());
        Assert.True(bass.Matches(PresetCatalog.Read(EqKind, slot)));
        Assert.True(Tuned().Matches(slot.Custom));
    }

    // ---------------------------------------------------------------- shortcuts

    private static (PresetGroups Groups, FakeActions Actions, PresetShortcuts Shortcuts, FakeLayers Mouse, PresetGroup Group) Shortcuts()
    {
        var actions = new FakeActions();
        var groups = new PresetGroups();
        var shortcuts = new PresetShortcuts(actions, groups, _ => { });
        var mouse = new FakeLayers();
        var group = new PresetGroup("mouse.trackpad", "Mouse and Trackpad", "Trackpad", new LayerPresets(mouse, InputFamily.Trackpad));
        groups.Register(group);
        return (groups, actions, shortcuts, mouse, group);
    }

    [Fact]
    public void AShortcut_RegistersAnApplyAction_UnderItsPageGroupAndPresets_AndRunsIt()
    {
        var (groups, actions, shortcuts, mouse, group) = Shortcuts();
        var entry = group.Provider.Find("2")!;
        Assert.Null(shortcuts.Set(group, entry, "Ctrl+Alt+F9"));

        var action = Assert.Single(actions.Actions);
        Assert.Equal(PresetShortcuts.ActionId("mouse.trackpad", "2"), action.Id);
        Assert.Equal("Switch to layer \"Trackpad 2\"", action.Title);
        Assert.Equal("Mouse and Trackpad - Trackpad - Presets", action.Heading);
        Assert.Equal("Ctrl+Alt+F9", shortcuts.ShortcutOf(group, "2"));

        action.Run();
        Assert.Equal(2, mouse.LayerOf(InputFamily.Trackpad));

        shortcuts.Remove(group, "2");
        Assert.Empty(actions.Actions);
        Assert.Equal(string.Empty, shortcuts.ShortcutOf(group, "2"));
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void AShortcut_IsRefused_WhenAnotherActionHasIt_OrItHasNoModifier()
    {
        var (groups, actions, shortcuts, _, group) = Shortcuts();
        actions.Register(new HotkeyAction("other", "Next mouse layer", "Mouse and Trackpad", "", _ => { }) { Gesture = "Ctrl+Alt+L" });
        var entry = group.Provider.Find("1")!;
        Assert.Contains("Next mouse layer", shortcuts.Set(group, entry, "Ctrl+Alt+L"));
        Assert.NotNull(shortcuts.Set(group, entry, "K"));
        Assert.Single(actions.Actions); // nothing was made
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void APresetsAction_FollowsARename_IsRemovedWithThePreset_AndComesBackWithAnUndo()
    {
        var actions = new FakeActions();
        var groups = new PresetGroups();
        using var shortcuts = new PresetShortcuts(actions, groups, _ => { });
        var store = PresetStore();
        var presets = new FxKindPresets(EqKind, store, _ => new FakeTarget { Content = Tuned() });
        var group = new PresetGroup(FxKindPresets.GroupId(EqKind), "Volume", EqKind.Name, presets);
        groups.Register(group);

        var id = presets.Add().Id!;
        shortcuts.Set(group, presets.Find(id)!, "Ctrl+Alt+F8");
        presets.Rename(id, "Desk");
        Assert.Equal("Apply preset \"Desk\"", Assert.Single(actions.Actions).Title);
        Assert.Equal("Ctrl+Alt+F8", actions.Actions[0].Gesture);

        var undo = presets.Delete(id);
        Assert.Empty(actions.Actions);
        undo!();
        Assert.Equal("Ctrl+Alt+F8", Assert.Single(actions.Actions).Gesture);
        groups.Dispose();
    }

    [Fact]
    public void AtStartUp_APresetWithASavedShortcut_GetsItsActionWhenItsGroupRegisters()
    {
        var actions = new FakeActions();
        actions.Saved[PresetShortcuts.ActionId("mouse.buttons", "1")] = "Ctrl+Alt+F7";
        actions.Saved[PresetShortcuts.ActionId("mouse.buttons", "99")] = "Ctrl+Alt+F6"; // no such layer: left alone
        var groups = new PresetGroups();
        using var shortcuts = new PresetShortcuts(actions, groups, _ => { });
        Assert.Empty(actions.Actions);

        groups.Register(new PresetGroup("mouse.buttons", "Mouse and Trackpad", "Mouse Buttons", new LayerPresets(new FakeLayers(), InputFamily.Mouse)));
        var action = Assert.Single(actions.Actions);
        Assert.Equal("Ctrl+Alt+F7", action.Gesture);
        groups.Dispose();
    }

    // ---------------------------------------------------------------- the window's model

    private static readonly PresetSection[] Sections =
    {
        new("Displays", "M0,0"), new("Volume", "M0,0"), new("OSD", "M0,0"), new("Mouse and Trackpad", "M0,0"), new("Gestures", "M0,0"), new("Shortcuts", "M0,0"),
    };

    /// The shell's order with nothing saved: the master list, the shortcuts, the opener, then the pages' groups (Displays with no monitor
    /// read yet, the layers), the effects' groups, the sections; the window's model is made last, as Open makes it. Nothing may need a
    /// piece that comes later, and empty data must not crash.
    private static (PresetGroups Groups, PresetShortcuts Shortcuts, PresetManagerViewModel Vm, FakeActions Actions) BuildInTheShellsOrder()
    {
        var actions = new FakeActions();
        var groups = new PresetGroups();
        var shortcuts = new PresetShortcuts(actions, groups, _ => { });
        var service = new PresetManagerService(groups, shortcuts, new PresetTriggersSlot(shortcuts), _ => { });
        var displays = new DisplaySettingsStore(null, _ => { });
        groups.Register(new PresetGroup("displays.hardware", "Displays", "Hardware",
            new BookPresets(new MonitorPresetBook(displays, () => Array.Empty<IMonitorPresetTarget>()))));
        groups.Register(new PresetGroup("displays.software", "Displays", "Software",
            new BookPresets(new PicturePresetBook(displays, () => new DisplayPreset(), _ => { }))));
        var mouse = new FakeLayers();
        groups.Register(new PresetGroup("mouse.buttons", "Mouse and Trackpad", "Mouse Buttons", new LayerPresets(mouse, InputFamily.Mouse)));
        groups.Register(new PresetGroup("mouse.trackpad", "Mouse and Trackpad", "Trackpad", new LayerPresets(mouse, InputFamily.Trackpad)));
        var store = PresetStore();
        groups.Register(PresetCatalog.Kinds.Select(k => new PresetGroup(FxKindPresets.GroupId(k), "Volume", k.Name, new FxKindPresets(k, store, _ => null))));
        service.UseSections(Sections);
        return (groups, shortcuts, new PresetManagerViewModel(groups, shortcuts, new PresetTriggersSlot(shortcuts), Sections), actions);
    }

    [Fact]
    public void BuildingEveryPieceInTheShellsOrder_WithNoData_Works()
    {
        var (groups, shortcuts, vm, _) = BuildInTheShellsOrder();
        Assert.Equal("Displays", vm.SelectedSection!.Title); // the first section with presets
        Assert.Equal(new[] { "Hardware", "Software" }, vm.Cards.Select(c => c.Title));
        Assert.False(vm.Cards[0].CanAdd);
        Assert.True(vm.Cards[0].IsEmpty);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void SectionsWithoutPresets_AreDimmed_AndCannotBeChosen()
    {
        var (groups, shortcuts, vm, _) = BuildInTheShellsOrder();
        Assert.Equal(new[] { true, true, false, true, false, false }, vm.Sections.Select(s => s.HasPresets));
        Assert.Equal("none", vm.Sections[2].CountText);
        Assert.Equal("2 groups", vm.Sections[0].CountText);

        vm.SelectedSection = vm.Sections[2];
        Assert.Equal("Displays", vm.SelectedSection!.Title);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Theory]
    [InlineData("Mouse and Trackpad", "mouse.buttons", "Mouse and Trackpad", "Mouse Buttons")]
    [InlineData("Mouse and Trackpad", "mouse.trackpad", "Mouse and Trackpad", "Trackpad")]
    [InlineData("Displays", "displays.software", "Displays", "Software")]
    [InlineData(null, "volume.fx.eq", "Volume", "Equaliser - Parametric")]
    [InlineData(null, null, "Displays", null)]
    [InlineData("OSD", null, "Displays", null)]
    public void Open_ChoosesTheSection_AndOutlinesTheCard(string? section, string? group, string expectedSection, string? expectedCard)
    {
        var (groups, shortcuts, vm, _) = BuildInTheShellsOrder();
        PresetCardViewModel? scrolled = null;
        vm.ScrollRequested += card => scrolled = card;
        vm.Navigate(section, group);
        Assert.Equal(expectedSection, vm.SelectedSection!.Title);
        Assert.Equal(expectedCard, vm.Cards.SingleOrDefault(c => c.IsTarget)?.Title);
        Assert.Equal(expectedCard, scrolled?.Title);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void Search_FindsPresetsInEverySection()
    {
        var (groups, shortcuts, vm, _) = BuildInTheShellsOrder();
        vm.Search = "trackpad 2";
        var card = Assert.Single(vm.Cards);
        Assert.Equal("Mouse and Trackpad - Trackpad", card.Title);
        Assert.Equal("Trackpad 2", Assert.Single(card.Rows).Name);
        Assert.Equal("1 preset found", vm.Heading);

        vm.Search = "nothing like it";
        Assert.True(vm.NothingFound);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void TheCard_GivesARowsShortcut_AndShowsARefusalOnTheWindowsLine()
    {
        var (groups, shortcuts, vm, actions) = BuildInTheShellsOrder();
        vm.Navigate(null, "mouse.trackpad");
        var card = vm.Cards.Single(c => c.Id == "mouse.trackpad");
        var row = card.Rows[1];

        row.Shortcut.Gesture = "Ctrl+Alt+F9"; // what the shortcut box does when keys are pressed
        Assert.Equal("Ctrl+Alt+F9", actions.Find(PresetShortcuts.ActionId("mouse.trackpad", row.Id))!.Gesture);
        Assert.False(vm.HasMessage);

        var other = card.Rows[2];
        other.Shortcut.Gesture = "Ctrl+Alt+F9"; // taken by the first
        Assert.True(vm.HasMessage);
        Assert.Equal(string.Empty, card.Rows[2].Shortcut.Gesture);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void AddingFromTheCard_PutsTheNewRowIntoRename()
    {
        var (groups, shortcuts, vm, _) = BuildInTheShellsOrder();
        vm.Navigate(null, "mouse.buttons");
        var card = vm.Cards.Single(c => c.Id == "mouse.buttons");
        card.AddCommand.Execute(null);
        var row = card.Rows.Single(r => r.Id == "2");
        Assert.True(row.IsEditing);
        row.EditText = "Games";
        row.CommitRename();
        Assert.False(row.IsEditing);
        Assert.Equal("Games", card.Rows.Single(r => r.Id == "2").Name);
        vm.Dispose();
        shortcuts.Dispose();
        groups.Dispose();
    }

    [Fact]
    public void GroupsRegisteredAfterTheWindowsModel_AppearAsCards()
    {
        var actions = new FakeActions();
        var groups = new PresetGroups();
        using var shortcuts = new PresetShortcuts(actions, groups, _ => { });
        using var vm = new PresetManagerViewModel(groups, shortcuts, new PresetTriggersSlot(shortcuts), Sections);
        Assert.Null(vm.SelectedSection);
        Assert.All(vm.Sections, s => Assert.False(s.HasPresets));

        groups.Register(new PresetGroup("mouse.buttons", "Mouse and Trackpad", "Mouse Buttons", new LayerPresets(new FakeLayers(), InputFamily.Mouse)));
        Assert.True(vm.Sections[3].HasPresets);
        vm.Navigate(null, "mouse.buttons");
        Assert.Equal("Mouse Buttons", Assert.Single(vm.Cards).Title);
        groups.Dispose();
    }
}
