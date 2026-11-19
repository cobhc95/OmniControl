using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Input.Gestures;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.App.Features.Osd;
using OmniControl.App.Shared.Ui.Restore;
using OmniControl.App.Features.Input.Rules;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.Tests;

/// "Restore defaults" on every page and "Restore all": what counts as the defaults, the copy taken first, the question, the undo, and the greyed reasons. Fakes only: no file, no Windows setting.
public class RestoreDefaultsTests
{
    private sealed class FakeStore : ISnapshotStore
    {
        public readonly Dictionary<string, Dictionary<string, string>> Files = new();
        public void Save(string key, IReadOnlyDictionary<string, string> copies) => Files[key] = new(copies);
        public Dictionary<string, string>? Load(string key) => Files.GetValueOrDefault(key);
    }

    /// A page whose whole state is one number; 0 is the default. Notes how many files were saved at its first change.
    private sealed class Counter : IRestorable
    {
        private readonly FakeStore? _store;
        public int Value;
        public string? Reason, Alert;
        public bool Fails, Throws;
        public int SavesAtFirstChange = -1;
        public readonly List<string> Log;

        public Counter(string key, int value, List<string> log, FakeStore? store = null) => (Key, Value, Log, _store) = (key, value, log, store);

        public string Key { get; }
        public string Name => Key.ToUpperInvariant();
        public string Scope => $"Puts {Key} back to 0.";
        public string? Warning => Alert;
        public string? Unavailable => Reason;
        public bool IsAtDefaults => Value == 0;
        public string CaptureSnapshot() => Value.ToString();

        public Task<bool> RestoreAsync() => Set(0);
        public Task<bool> UndoAsync(string snapshot) => Set(int.Parse(snapshot));

        private Task<bool> Set(int value)
        {
            if (SavesAtFirstChange < 0) SavesAtFirstChange = _store?.Files.Count ?? 0;
            if (Throws) throw new InvalidOperationException("a service is not running");
            Value = value;
            Log.Add($"{Key}={value}");
            return Task.FromResult(!Fails);
        }
    }

    private static RestoreRunner One(Counter part, FakeStore store, Func<RestoreQuestion, Task<bool>>? confirm = null) =>
        new(part.Key, part.Name, new[] { part }, false, confirm ?? (_ => Task.FromResult(true)), store);

    // ---- the one comparison / copy / undo every page shares ----

    [Fact]
    public async Task SettingsRestore_round_trips_through_its_snapshot()
    {
        var value = new OsdSettings { Enabled = false, Opacity = 40, Excluded = { "Volume" } };
        var applied = new List<OsdSettings>();
        var part = new SettingsRestore<OsdSettings>("osd", "OSD", () => "scope", () => value, () => new OsdSettings(),
            target => { applied.Add(target); value = target; return Task.FromResult(true); });

        Assert.False(part.IsAtDefaults);
        var before = part.CaptureSnapshot();
        Assert.True(await part.RestoreAsync());
        Assert.True(part.IsAtDefaults);
        Assert.True(await part.UndoAsync(before));
        Assert.False(part.IsAtDefaults);
        Assert.Equal(40, value.Opacity);
        Assert.Equal(new[] { "Volume" }, value.Excluded);
    }

    [Fact]
    public void OSD_defaults_are_a_new_OsdSettings()
    {
        var part = new SettingsRestore<OsdSettings>("osd", "OSD", () => "scope", () => new OsdSettings(), () => new OsdSettings(), _ => Task.FromResult(true));
        Assert.True(part.IsAtDefaults);
        var changed = new OsdSettings { HideWindowsOsd = true };
        Assert.NotEqual(JsonSerializer.Serialize(new OsdSettings()), JsonSerializer.Serialize(changed));
    }

    // ---- the runner: greyed reasons, the question, the copy first, the undo ----

    [Fact]
    public void Button_is_grey_with_the_reason_when_at_defaults_or_unavailable()
    {
        var log = new List<string>();
        var part = new Counter("a", 0, log);
        var runner = One(part, new FakeStore());
        Assert.Equal(RestoreWords.AtDefaults, runner.Blocked);

        part.Value = 3;
        Assert.Null(runner.Blocked);

        part.Reason = "A plug-in scan is running";
        Assert.Equal("A plug-in scan is running", runner.Blocked);
    }

    [Fact]
    public async Task Running_when_blocked_changes_nothing_and_says_why()
    {
        var log = new List<string>();
        var result = await One(new Counter("a", 0, log), new FakeStore()).RunAsync();
        Assert.False(result.Ran);
        Assert.Equal(RestoreWords.AtDefaults + ".", result.Line);
        Assert.Empty(log);
    }

    [Fact]
    public async Task Declined_question_changes_nothing_and_saves_nothing()
    {
        var log = new List<string>();
        var store = new FakeStore();
        var part = new Counter("a", 5, log, store);
        var result = await One(part, store, _ => Task.FromResult(false)).RunAsync();
        Assert.False(result.Ran);
        Assert.Equal(string.Empty, result.Line);
        Assert.Equal(5, part.Value);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task The_copy_is_saved_before_the_first_change_and_undo_puts_it_back()
    {
        var log = new List<string>();
        var store = new FakeStore();
        var part = new Counter("a", 5, log, store);
        var runner = One(part, store);

        var done = await runner.RunAsync();
        Assert.True(done.Ran);
        Assert.True(done.CanUndo);
        Assert.Equal(RestoreWords.Done("A"), done.Line);
        Assert.Equal(1, part.SavesAtFirstChange); // the file existed when the first value changed
        Assert.Equal("5", store.Files["a"]["a"]);
        Assert.Equal(0, part.Value);

        var undone = await runner.UndoAsync();
        Assert.Equal(RestoreWords.Undone, undone.Line);
        Assert.Equal(5, part.Value);
    }

    [Fact]
    public async Task Undo_reads_the_saved_file_when_the_running_copy_is_gone()
    {
        var log = new List<string>();
        var store = new FakeStore();
        var part = new Counter("a", 5, log, store);
        await One(part, store).RunAsync();

        var fresh = One(part, store); // a new run of the program: only the file is left
        Assert.Equal(RestoreWords.Undone, (await fresh.UndoAsync()).Line);
        Assert.Equal(5, part.Value);
        Assert.Equal(RestoreWords.NoCopy, (await One(new Counter("zzz", 1, log), new FakeStore()).UndoAsync()).Line);
    }

    // ---- Restore all ----

    private static (RestoreRunner Runner, Counter[] Parts, FakeStore Store, List<RestoreQuestion> Asked, List<string> Log) All(int a, int b, int c)
    {
        var log = new List<string>();
        var store = new FakeStore();
        var parts = new[] { new Counter("a", a, log, store), new Counter("b", b, log, store), new Counter("c", c, log, store) };
        var asked = new List<RestoreQuestion>();
        var runner = new RestoreRunner("all", "All", parts, true, q => { asked.Add(q); return Task.FromResult(true); }, store);
        return (runner, parts, store, asked, log);
    }

    [Fact]
    public async Task Restore_all_asks_once_listing_every_section_and_takes_one_combined_copy_first()
    {
        var (runner, parts, store, asked, log) = All(1, 2, 3);
        var result = await runner.RunAsync();

        var question = Assert.Single(asked);
        foreach (var part in parts) Assert.Contains($"{part.Name}: {part.Scope}", question.Text);
        Assert.Equal(new[] { "a=0", "b=0", "c=0" }, log);
        Assert.Equal(new[] { "a", "b", "c" }, store.Files["all"].Keys);
        Assert.All(parts, p => Assert.Equal(1, p.SavesAtFirstChange));
        Assert.Equal(RestoreWords.Done("All"), result.Line);

        var undone = await runner.UndoAsync();
        Assert.Equal(RestoreWords.Undone, undone.Line);
        Assert.Equal(new[] { 1, 2, 3 }, parts.Select(p => p.Value));
    }

    [Fact]
    public async Task Restore_all_skips_pages_already_at_their_defaults()
    {
        var (runner, parts, _, _, log) = All(0, 2, 0);
        await runner.RunAsync();
        Assert.Equal(new[] { "b=0" }, log);
        Assert.Equal(RestoreWords.AtDefaults, All(0, 0, 0).Runner.Blocked);
    }

    [Fact]
    public async Task A_failing_section_is_named_in_one_sentence_and_the_others_still_go_on()
    {
        var (runner, parts, _, _, _) = All(1, 2, 3);
        parts[1].Throws = true;
        parts[2].Fails = true;

        var result = await runner.RunAsync();
        Assert.Equal(RestoreWords.Failed(new[] { "B", "C" }), result.Line);
        Assert.Equal(0, parts[0].Value);
        Assert.Equal(2, parts[1].Value);
        Assert.True(result.CanUndo);
    }

    [Fact]
    public async Task An_unavailable_page_is_left_out_of_restore_all_and_a_bug_is_not_swallowed()
    {
        var (runner, parts, _, _, _) = All(1, 2, 3);
        parts[0].Reason = "scanning";
        await runner.RunAsync();
        Assert.Equal(1, parts[0].Value);
        Assert.Equal(0, parts[1].Value);

        var (second, others, _, _, _) = All(1, 2, 3);
        others[0].Reason = "x";
        others[1].Reason = "y";
        others[2].Reason = "z";
        Assert.Equal("x", second.Blocked);
    }

    // ---- Mouse and Trackpad's gestures: custom rules are deleted only after the stronger line ----

    [Fact]
    public void Deleting_custom_rules_adds_a_stronger_line_and_a_stronger_button()
    {
        Assert.Null(MouseRestoreState.WarningOf(0));
        Assert.Equal("Deletes your custom rule", MouseRestoreState.WarningOf(1));
        Assert.Equal("Deletes your 3 custom rules", MouseRestoreState.WarningOf(3));

        var log = new List<string>();
        var plain = new Counter("gestures", 1, log);
        var strong = new Counter("gestures", 1, log) { Alert = MouseRestoreState.WarningOf(2) };
        var calm = RestoreWords.Ask(plain);
        var stern = RestoreWords.Ask(strong);
        Assert.Equal("Restore", calm.Button);
        Assert.DoesNotContain("Deletes", calm.Text);
        Assert.Equal("Restore anyway", stern.Button);
        Assert.Contains("Deletes your 2 custom rules.", stern.Text);
        Assert.Equal("Restore anyway", RestoreWords.AskAll(new[] { plain, strong }).Button);
    }

    [Fact]
    public void Gestures_are_at_their_defaults_when_every_rule_is_at_its_default_with_its_default_action_and_no_rules_exist()
    {
        var current = new MouseRestoreState { Mouse = MouseSettings.Defaults() };
        Assert.True(MouseRestoreState.Same(current, MouseRestoreState.DefaultsOf(current, null)));

        var trigger = GestureCatalog.Triggers[0];
        current.Gestures.RuleFor(trigger); // created lazily by the page: still the defaults
        Assert.True(MouseRestoreState.Same(current, MouseRestoreState.DefaultsOf(current, null)));

        current.Gestures.RuleFor(trigger).On = !trigger.DefaultOn;
        Assert.False(MouseRestoreState.Same(current, MouseRestoreState.DefaultsOf(current, null)));

        var withRule = new MouseRestoreState { Mouse = MouseSettings.Defaults() };
        withRule.Rules.Rules.Add(new Rule { Name = "mine" });
        Assert.False(MouseRestoreState.Same(withRule, MouseRestoreState.DefaultsOf(withRule, null)));
    }

    [Fact]
    public void Gestures_copy_resets_the_rule_objects_in_place_so_the_cards_follow()
    {
        var settings = new GestureSettings { Enabled = false, EdgeStrip = 20 };
        var trigger = GestureCatalog.Triggers[0];
        var rule = settings.RuleFor(trigger);
        rule.On = !trigger.DefaultOn;
        rule.Action = "something";
        rule.Options["x"] = "1";

        settings.CopyFrom(new GestureSettings());
        Assert.Same(rule, settings.RuleFor(trigger));
        Assert.Equal(trigger.DefaultOn, rule.On);
        Assert.Equal(trigger.DefaultAction, rule.Action);
        Assert.Empty(rule.Options);
        Assert.True(settings.Enabled);
        Assert.Equal(GestureSettings.DefaultEdgeStrip, settings.EdgeStrip);
    }

    // ---- Shortcuts ----

    [Fact]
    public void Shortcuts_are_at_their_defaults_when_no_action_has_a_key_or_a_device_pick()
    {
        var none = new Dictionary<string, StoredHotkey> { ["a"] = HotkeyDefaults.Cleared, ["b"] = new("", false, new List<string> { "old" }) };
        Assert.True(HotkeyDefaults.IsAtDefaults(none));

        var bound = new Dictionary<string, StoredHotkey> { ["a"] = new("Ctrl+Alt+K", false, new()), ["b"] = new("", true, new List<string> { "d1" }) };
        Assert.False(HotkeyDefaults.IsAtDefaults(bound));
        Assert.True(HotkeyDefaults.IsAtDefaults(HotkeyDefaults.Of(bound)));
        Assert.Equal(new[] { "a", "b" }, HotkeyDefaults.Of(bound).Keys);
    }

    // ---- Mouse and Trackpad ----

    [Fact]
    public void Mouse_defaults_equal_a_loaded_default_file_and_notice_a_mapping()
    {
        var state = new MouseRestoreState { Mouse = MouseSettings.Defaults() };
        Assert.True(MouseRestoreState.Same(state, MouseRestoreState.DefaultsOf(state, null)));

        state.Mouse.Bindings["1.Middle"] = "Copy";
        Assert.False(MouseRestoreState.Same(state, MouseRestoreState.DefaultsOf(state, null)));
    }

    [Fact]
    public void Mouse_copy_covers_every_stored_setting()
    {
        var writable = typeof(MouseSettings).GetProperties().Where(p => p.CanWrite && p.GetMethod!.IsPublic).Select(p => p.Name).ToList();
        Assert.Equal(15, writable.Count); // a new stored setting must be added to CopyFrom (and to this list)

        var source = new MouseSettings
        {
            OnlyUsedLayers = false, BlockAutoscroll = false, TrackpadOnlyUsedLayers = false, TrackpadNamesIndependent = true,
            Bindings = { ["2.Back"] = "Copy" }, ExtraActions = { ["2.Back"] = new List<string> { "Paste" } },
            LayerNames = { [2] = "Games" }, TrackpadLayerNames = new() { [3] = "Pad" },
            ButtonsScope = new(ScopeMode.Only, "list1"), TrackpadScope = new(ScopeMode.Except, "list4"), CardScopes = { ["Back"] = new(ScopeMode.Except, "list2") },
            PauseInFullScreen = true, NeverActIn = new(ScopeMode.Only, "list3"), RunAsAdministrator = true,
        };
        var target = MouseSettings.Defaults();
        target.CopyFrom(source);
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(target));
    }

    [Fact]
    public void Pointer_settings_compare_without_the_day_the_copy_was_made()
    {
        var cursors = new CursorSnapshot(null, null, new Dictionary<string, CursorValue?>());
        var original = new PointerOriginal(new Dictionary<string, int> { ["Speed"] = 10 }, cursors, new DateTime(2020, 1, 1));
        var now = new PointerOriginal(new Dictionary<string, int> { ["Speed"] = 10 }, cursors, DateTime.Now);
        var current = new MouseRestoreState { Mouse = MouseSettings.Defaults(), Pointer = now };

        Assert.True(MouseRestoreState.Same(current, MouseRestoreState.DefaultsOf(current, original)));
        now.Values["Speed"] = 14;
        Assert.False(MouseRestoreState.Same(current, MouseRestoreState.DefaultsOf(current, original)));

        var untouched = new MouseRestoreState { Mouse = MouseSettings.Defaults() }; // OmniControl never changed a pointer setting
        Assert.Null(MouseRestoreState.DefaultsOf(untouched, original).Pointer);
        Assert.True(MouseRestoreState.Same(untouched, MouseRestoreState.DefaultsOf(untouched, original)));
    }

    // ---- Displays ----

    [Fact]
    public void Displays_defaults_are_a_neutral_picture_with_warm_light_off_and_presets_are_not_part_of_it()
    {
        var current = new DisplayRestoreState { WarmLight = true };
        current.Picture.Displays["\\\\.\\DISPLAY1"] = new PictureValues { Brightness = 70, Warmth = 20 };
        current.Picture.Invert = true;

        var defaults = DisplayRestoreState.DefaultsOf(current);
        Assert.False(defaults.WarmLight);
        Assert.False(defaults.Picture.Invert);
        Assert.Equal(new PictureValues().Brightness, defaults.Picture.Displays["\\\\.\\DISPLAY1"].Brightness);
        Assert.Equal(new[] { "\\\\.\\DISPLAY1" }, defaults.Picture.Displays.Keys);
        Assert.Empty(defaults.Picture.Name);

        var part = new SettingsRestore<DisplayRestoreState>("displays", "Displays", () => DisplayRestoreState.Scope, () => current,
            () => DisplayRestoreState.DefaultsOf(current), _ => Task.FromResult(true));
        Assert.False(part.IsAtDefaults);
        Assert.Contains("Monitor settings are not changed", part.Scope);
        Assert.Contains("presets are kept", part.Scope);
        Assert.True(new SettingsRestore<DisplayRestoreState>("d", "D", () => "", () => defaults, () => DisplayRestoreState.DefaultsOf(defaults), _ => Task.FromResult(true)).IsAtDefaults);
    }

    // ---- App settings ----

    [Fact]
    public void App_settings_defaults_are_theme_system_accent_windows_and_the_rest_off()
    {
        var defaults = AppRestoreState.Defaults();
        Assert.Equal(ThemeChoice.System, defaults.Theme);
        Assert.Equal(AccentService.WindowsName, defaults.Accent);
        Assert.False(defaults.StartWithWindows);
        Assert.False(defaults.StartMinimised);
        Assert.Equal(CloseBehavior.Ask, defaults.CloseBehavior);
        Assert.Empty(defaults.PluginFolders);
        Assert.Contains("Ask each time", AppRestoreState.Scope());

        var changed = new AppSettings { Theme = ThemeChoice.Dark, StartMinimised = true, PluginFolders = { "C:\\Plugins" }, VolumeView = VolumeView.Advanced };
        var current = AppRestoreState.Of(changed, true);
        var part = new SettingsRestore<AppRestoreState>("app", "App settings", AppRestoreState.Scope, () => current, AppRestoreState.Defaults, _ => Task.FromResult(true));
        Assert.False(part.IsAtDefaults);
        Assert.True(new SettingsRestore<AppRestoreState>("app", "App settings", AppRestoreState.Scope, () => AppRestoreState.Of(new AppSettings { VolumeView = VolumeView.Easy }, false),
            AppRestoreState.Defaults, _ => Task.FromResult(true)).IsAtDefaults); // the Volume view is not one of the settings it restores
    }
}
