using System.Text.Json;
using OmniControl.App.Display;
using OmniControl.App.Pages;
using OmniControl.App.Pages.Displays;
using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// <summary>
/// The Hardware group's monitor presets over fake monitors (no monitor is read or written): what a preset keeps, which connected
/// monitor an entry belongs to, matching, what applying writes (range, missing features, never power), the per-monitor memory, the
/// greyed drop-down, loading an older settings file, and the order the page builds its pieces in.
/// </summary>
public class MonitorPresetTests
{
    private const int Brightness = VcpCode.Brightness, Contrast = VcpCode.Contrast, Input = VcpCode.InputSource, Gamma = 0x72, Power = VcpCode.PowerMode;

    /// <summary>A fake display: its features (value, maximum, accepted raw values) and every write it was given.</summary>
    private sealed class FakeMonitor : IMonitorPresetTarget
    {
        private readonly Dictionary<int, LiveFeature> _features = new();

        public FakeMonitor(string id, string name) => (Id, Name) = (id, name);

        public string Id { get; set; }
        public string Name { get; }
        public List<PlannedWrite> Written { get; } = new();

        public FakeMonitor Level(int code, int percent, uint max = 100) { _features[code] = new LiveFeature(code, percent, max, Array.Empty<uint>()); return this; }

        public FakeMonitor Choice(int code, int raw, params uint[] allowed) { _features[code] = new LiveFeature(code, raw, 0, allowed); return this; }

        public int ValueOf(int code) => _features[code].Value;

        public LiveMonitor? Snapshot() => _features.Count == 0 ? null : new LiveMonitor(Id, Name, _features.Values.ToList());

        public void Write(IReadOnlyList<PlannedWrite> writes)
        {
            Written.AddRange(writes);
            foreach (var w in writes) _features[w.Code] = _features[w.Code] with { Value = w.Value };
        }
    }

    private static FakeMonitor Dell() => new FakeMonitor(@"\\.\DISPLAY1", "DELL U2720Q").Level(Brightness, 70).Level(Contrast, 40).Choice(Input, 0x0F, 0x0F, 0x11);

    private static FakeMonitor Lg() => new FakeMonitor(@"\\.\DISPLAY2", "LG 27GL850").Level(Brightness, 30, 255).Level(Gamma, 60);

    private static DisplaySettingsStore Store(DisplaySettings? data = null) => new(data, _ => { });

    private static MonitorPresetBook Book(DisplaySettingsStore store, params FakeMonitor[] monitors) =>
        new(store, () => monitors.ToList<IMonitorPresetTarget>());

    // ---- capture and apply ----

    [Fact]
    public void SaveAndApply_RoundTripsEachMonitorWithItsOwnFeatures()
    {
        var (dell, lg) = (Dell(), Lg());
        var store = Store();
        using var book = Book(store, dell, lg);
        book.SaveCurrent("Work");

        var work = store.Monitors.Find("Work")!;
        Assert.Equal(new[] { "DELL U2720Q", "LG 27GL850" }, work.Monitors.Select(m => m.Name));
        Assert.Equal(new[] { Brightness, Contrast, Input }, work.Monitors[0].Values.Select(v => v.Code).OrderBy(c => c));
        Assert.Equal(new[] { Brightness, Gamma }, work.Monitors[1].Values.Select(v => v.Code).OrderBy(c => c));

        dell.Level(Brightness, 10).Level(Contrast, 90).Choice(Input, 0x11, 0x0F, 0x11);
        lg.Level(Brightness, 99, 255).Level(Gamma, 5);
        book.Apply("Work");

        Assert.Equal((70, 40, 0x0F), (dell.ValueOf(Brightness), dell.ValueOf(Contrast), dell.ValueOf(Input)));
        Assert.Equal((30, 60), (lg.ValueOf(Brightness), lg.ValueOf(Gamma)));
        Assert.DoesNotContain(dell.Written, w => w.Code == Gamma);
        Assert.DoesNotContain(lg.Written, w => w.Code is Contrast or Input);
    }

    [Fact]
    public void Apply_WritesOnlyWhatDiffers()
    {
        var dell = Dell();
        var store = Store();
        using var book = Book(store, dell);
        book.SaveCurrent("Work");
        dell.Level(Contrast, 10);
        book.Apply("Work");
        Assert.Equal(new[] { Contrast }, dell.Written.Select(w => w.Code));
    }

    [Fact]
    public void Apply_ClipsLevelsToTheRange_SkipsRefusedChoices_AndNeverWritesPower()
    {
        var dell = Dell();
        var entry = new MonitorEntry
        {
            Name = dell.Name, Id = dell.Id,
            Values =
            {
                new FeatureValue { Code = Brightness, Value = 150 },
                new FeatureValue { Code = Contrast, Value = -20 },
                new FeatureValue { Code = Input, Value = 0x99 },       // the monitor does not list it
                new FeatureValue { Code = Power, Value = (int)VcpCode.Power.Off },
                new FeatureValue { Code = 0x05, Value = 1 },            // a reset action: not a feature a preset keeps
            },
        };

        var writes = MonitorPresetRules.Plan(entry, dell.Snapshot()!);

        Assert.Equal(new[] { (Brightness, 100), (Contrast, 0) }, writes.Select(w => (w.Code, w.Value)));
    }

    [Fact]
    public void Capture_KeepsNoPowerAndNoUnknownCode_AndNothingForAMonitorWithNoFeature()
    {
        var odd = new FakeMonitor("x", "ODD").Level(Brightness, 50).Choice(Power, 1, 1, 4).Level(0x05, 1);
        var bare = new FakeMonitor("y", "BARE");
        var entries = MonitorPresetRules.Capture(new[] { odd.Snapshot()!, new LiveMonitor("y", "BARE", Array.Empty<LiveFeature>()) });
        Assert.Single(entries);
        Assert.Equal(new[] { Brightness }, entries[0].Values.Select(v => v.Code));
        Assert.Null(bare.Snapshot());
    }

    [Fact]
    public void MonitorMissing_IsLeftAlone_AndItsEntryStaysSaved()
    {
        var (dell, lg) = (Dell(), Lg());
        var store = Store();
        using var book = Book(store, dell, lg);
        book.SaveCurrent("Work");

        dell.Level(Brightness, 5);
        lg.Level(Brightness, 5, 255);
        using var onlyDell = Book(store, dell);
        onlyDell.Apply("Work");

        Assert.Equal(70, dell.ValueOf(Brightness));
        Assert.Empty(lg.Written);
        Assert.Equal(new[] { new PresetPart("DELL U2720Q", true), new PresetPart("LG 27GL850", false) }, onlyDell.PartsOf("Work"));

        // Saving again with the LG away keeps its entry.
        dell.Level(Brightness, 20);
        onlyDell.SaveCurrent("Work");
        var work = store.Monitors.Find("Work")!;
        Assert.Equal(2, work.Monitors.Count);
        Assert.Equal(20, work.Monitors.Single(m => m.Name == "DELL U2720Q").Values.Single(v => v.Code == Brightness).Value);
    }

    [Fact]
    public void AMonitorWithNoEntry_IsLeftAlone()
    {
        var dell = Dell();
        var store = Store();
        using var onlyDell = Book(store, dell);
        onlyDell.SaveCurrent("Work");
        var lg = Lg();
        lg.Level(Brightness, 1, 255);
        using var both = Book(store, dell, lg);
        both.Apply("Work");
        Assert.Empty(lg.Written);
    }

    // ---- matching ----

    [Fact]
    public void Matches_WhenEveryStoredValueOfAConnectedMonitorEqualsTheLiveOne()
    {
        var (dell, lg) = (Dell(), Lg());
        var store = Store();
        using var book = Book(store, dell, lg);
        book.SaveCurrent("Work");
        var work = store.Monitors.Find("Work")!;
        var live = new[] { dell.Snapshot()!, lg.Snapshot()! };

        Assert.True(MonitorPresetRules.Matches(work, live));
        dell.Level(Contrast, 41);
        Assert.False(MonitorPresetRules.Matches(work, new[] { dell.Snapshot()!, lg.Snapshot()! }));
    }

    [Fact]
    public void Matches_ReadsAMonitorWhoseRangeIsNotPercentByTheValueItReadsBack()
    {
        var lg = Lg().Level(Brightness, 37, 255);
        var store = Store();
        using var book = Book(store, lg);
        book.SaveCurrent("Mine");
        // Written as 94 of 255 and read again: 36 percent.
        lg.Level(Brightness, 36, 255);
        Assert.Equal("Mine", book.Shown);
    }

    [Fact]
    public void APresetOfMonitorsThatAreAllAway_ShowsNothing()
    {
        var store = Store();
        using var book = Book(store, Dell());
        book.SaveCurrent("Work");
        using var other = Book(store, Lg());
        Assert.Null(other.Shown);
    }

    [Fact]
    public void AMonitorOnAnotherPort_StillMatchesByItsUniqueName()
    {
        var dell = Dell();
        var store = Store();
        using var book = Book(store, dell);
        book.SaveCurrent("Work");

        dell.Id = @"\\.\DISPLAY3";
        dell.Level(Brightness, 5);
        Assert.Null(book.Shown);
        book.Apply("Work");
        Assert.Equal(70, dell.ValueOf(Brightness));
        Assert.Equal("Work", book.Shown);
    }

    [Fact]
    public void TwoMonitorsOfTheSameName_AreNotGuessedAcrossPorts()
    {
        var entries = new List<MonitorEntry> { new() { Name = "SAME", Id = "a" }, new() { Name = "SAME", Id = "b" } };
        var live = new[] { new LiveMonitor("c", "SAME", Array.Empty<LiveFeature>()) };
        Assert.Empty(MonitorPresetRules.Pair(entries, live));
    }

    // ---- per-monitor memory ----

    [Fact]
    public void TheLastPresetAppliedToAMonitor_IsTheOneNamedWhenSeveralMatch()
    {
        var dell = Dell();
        var store = Store();
        using var book = Book(store, dell);
        book.SaveCurrent("First");
        book.SaveCurrent("Second");   // the same values under another name
        Assert.Equal("Second", book.Shown);

        book.Apply("First");
        Assert.Equal("First", book.Shown);
        Assert.Equal("First", store.LastMonitorPreset("DELL U2720Q"));
    }

    [Fact]
    public void Memory_FollowsARename_AndForgetsADelete_AndIsSaved()
    {
        var saved = new List<string>();
        var store = new DisplaySettingsStore(null, d => saved.Add(JsonSerializer.Serialize(d)));
        using var book = Book(store, Dell());
        book.SaveCurrent("First");
        book.SaveCurrent("Second");
        book.Apply("First");

        Assert.Null(book.Rename("First", "Home"));
        Assert.Equal("Home", store.LastMonitorPreset("DELL U2720Q"));

        book.Delete("Home");
        Assert.Null(store.LastMonitorPreset("DELL U2720Q"));
        Assert.Contains("MonitorLastPreset", saved.Last());
    }

    [Fact]
    public void Next_CyclesTheSavedPresets()
    {
        var dell = Dell();
        var store = Store();
        using var book = Book(store, dell);
        dell.Level(Brightness, 10);
        book.SaveCurrent("Dim");
        dell.Level(Brightness, 90);
        book.SaveCurrent("Bright");

        book.Next();
        Assert.Equal(10, dell.ValueOf(Brightness));
        book.Next();
        Assert.Equal(90, dell.ValueOf(Brightness));
        book.Next();
        Assert.Equal(10, dell.ValueOf(Brightness));
    }

    // ---- the drop-down ----

    [Fact]
    public void NoMonitorControls_GreysTheDropDownWithItsReason()
    {
        var store = Store();
        using var book = Book(store, new FakeMonitor("laptop", "Built-in display"));
        using var picker = new ProviderChoice(book.Label, new BookPresets(book), () => { }, withCustom: true);
        Assert.False(picker.Choice.Availability.IsAvailable);
        Assert.Equal(ProviderChoice.NoneText, picker.Choice.Selected);
        Assert.Equal(MonitorPresetBook.NoControls, picker.Choice.Availability.Reason);
    }

    [Fact]
    public void TheDropDown_ListsPresets_FollowsTheLiveValues_AndPickingApplies()
    {
        var dell = Dell();
        var store = Store();
        using var book = Book(store, dell);
        using var picker = new ProviderChoice(book.Label, new BookPresets(book), () => { }, withCustom: true);
        Assert.True(picker.Choice.Availability.IsAvailable);
        Assert.Equal(ProviderChoice.CustomText, picker.Choice.Selected);

        book.SaveCurrent("Work");
        Assert.Equal("Work", picker.Choice.Selected);

        dell.Level(Brightness, 12);
        picker.Refresh();
        Assert.Equal(ProviderChoice.CustomText, picker.Choice.Selected);

        picker.Choice.Selected = "Work";
        Assert.Equal(70, dell.ValueOf(Brightness));
        Assert.Equal("Work", picker.Choice.Selected);
    }

    [Fact]
    public void ThePresetManagersCard_ListsMonitorsWithTheirState_AddsAndDeletesWithUndo()
    {
        var (dell, lg) = (Dell(), Lg());
        var store = Store();
        using var book = Book(store, dell, lg);
        var card = new BookPresets(book);

        var made = card.Add();
        Assert.Null(made.Problem);
        var entry = Assert.Single(card.Entries);
        Assert.Equal(made.Id, entry.Id);
        Assert.Equal("Monitors", entry.Name);
        Assert.True(entry.InUse);
        Assert.Equal(2, entry.Tags.Count);
        Assert.All(entry.Tags, t => Assert.True(t.IsGood)); // both monitors are connected

        var undo = card.Delete(entry.Id);
        Assert.Empty(card.Entries);
        undo!();
        Assert.Equal(entry.Id, Assert.Single(card.Entries).Id); // back with the same id, so its shortcut follows
    }

    // ---- storage ----

    [Fact]
    public void AnOlderSettingsFile_LoadsUnchanged_AndTheNewPartsRoundTrip()
    {
        const string old = """{"WarmLight":{"Enabled":true,"Start":1260,"End":420,"Kelvin":3400},"Presets":[{"Name":"Night","Displays":{},"Any":{},"Saturation":100,"Invert":false}]}""";
        var data = JsonSerializer.Deserialize<DisplaySettings>(old)!;
        var store = Store(data);

        Assert.True(store.WarmLight.Enabled);
        Assert.Equal("Night", store.Presets.Single().Name);
        Assert.Empty(store.Monitors.Items);

        using var book = Book(store, Dell());
        book.SaveCurrent("Work");
        var again = Store(JsonSerializer.Deserialize<DisplaySettings>(JsonSerializer.Serialize(store.Data)));
        Assert.Equal("Work", again.Monitors.Items.Single().Name);
        Assert.Equal(3, again.Monitors.Items.Single().Monitors.Single().Values.Count);
        Assert.Equal("Work", again.LastMonitorPreset("DELL U2720Q"));
        Assert.Equal("Night", again.Presets.Single().Name);
    }

    [Fact]
    public void DamagedMonitorEntries_AreDropped()
    {
        const string damaged = """{"MonitorPresets":[null,{"Name":"","Monitors":[]},{"Name":"Ok","Monitors":[null,{"Name":"A","Id":null,"Values":null},{"Name":"B","Values":[]}]}],"MonitorLastPreset":null}""";
        var store = Store(JsonSerializer.Deserialize<DisplaySettings>(damaged));
        var ok = Assert.Single(store.Monitors.Items);
        Assert.Equal(new[] { "B" }, ok.Monitors.Select(m => m.Name));
    }

    // ---- start-up ----

    [Fact]
    public void BuildingThePagesPiecesInTheConstructorsOrder_NeedsNothingThatDoesNotExistYet()
    {
        // DisplaysPageViewModel's constructor makes: the store, then the picture book and its drop-down, then the monitor book and its
        // drop-down. The drop-downs read their list as soon as they are made, when no display has been read (the list of displays is
        // empty): the monitor drop-down must then be greyed, not crash.
        var hardware = new List<FakeMonitor>();
        var store = Store();
        var picture = new PicturePresetBook(store, () => new DisplayPreset(), _ => { });
        using var pictures = new ProviderChoice(picture.Label, new BookPresets(picture), () => { }, withCustom: true);
        using var monitorBook = new MonitorPresetBook(store, () => hardware.ToList<IMonitorPresetTarget>());
        using var monitors = new ProviderChoice(monitorBook.Label, new BookPresets(monitorBook), () => { }, withCustom: true);

        Assert.True(pictures.Choice.Availability.IsAvailable);
        Assert.False(monitors.Choice.Availability.IsAvailable);

        hardware.Add(Dell());
        monitors.Refresh();
        Assert.True(monitors.Choice.Availability.IsAvailable);
        Assert.Equal(ProviderChoice.CustomText, monitors.Choice.Selected);
    }
}
