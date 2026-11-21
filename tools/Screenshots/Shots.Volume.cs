using Avalonia.Layout;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Volume page's Mixer tab. Its view-model cannot be made here: it starts the mixer's scans (which write saved levels and routes to
    /// programs' sessions) and the effects engine (which writes Equalizer APO's config and may change the default device). So the rows are
    /// the app's own row view-models and row views (VolumeRowView, MixerHeadings) with sample values, set out the way VolumePage.axaml sets
    /// out its Mixer tab (title line with Easy | Advanced, tabs and preset picker, the Outputs and Microphones group cards).
    private static void VolumeMixer(Camera camera)
    {
        Use(ViewTemplate.For<VolumeRow, VolumeRowView>());
        var outputs = new VolumeGroup("Volume/Outputs", "Outputs") { ExpandedByDefault = true, SummaryBesideName = true, RouteColumn = "Plays on", ShowsPan = true };
        var inputs = new VolumeGroup("Volume/Inputs", "Microphones") { ExpandedByDefault = true, RouteColumn = "Records from", ShowsListen = true };

        var master = new MixerItem(MixerService.MasterKey, true)
        {
            Name = "Master",
            PlaysOn = new PlaysOnMenu(new ControlGate(), _ => { }, marksCurrent: true),
            RouteChoice = RouteChoice.ForMaster(_ => { }, () => { }),
        };
        master.Sync(true, 42, 0, false, fxOn: true, fxCount: 3);
        Fill(master, "Speakers", RunMethod.Apo, runs: true);
        outputs.Items.Add(master);
        foreach (var (name, volume, pan, fx, method, plays) in new[]
                 {
                     ("Browser", 80, 0, 0, RunMethod.None, "Speakers"),
                     ("Music player", 65, -20, 2, RunMethod.Recording, "Speakers"),
                     ("Video call", 90, 0, 1, RunMethod.Recording, "Headphones"),
                     ("Game", 55, 10, 0, RunMethod.None, "Headphones"),
                 })
        {
            var row = new MixerItem("sample." + name, false)
            {
                Name = name,
                PlaysOn = new PlaysOnMenu(new ControlGate(), _ => { }, marksCurrent: true),
                RouteChoice = RouteChoice.ForProgram(_ => { }, () => { }),
            };
            row.Sync(true, volume, pan, false, fxOn: fx > 0, fxCount: fx);
            row.StateWord = name is "Music player" or "Video call" ? "playing" : string.Empty;
            Fill(row, plays, method, runs: fx > 0);
            outputs.Items.Add(row);
        }
        var mic = new InputItem("sample.mic") { Name = "Microphone", RouteChoice = RouteChoice.ForMicrophone(_ => { }, () => { }) };
        mic.Sync(75, false, fxOn: true, fxCount: 2);
        Fill(mic, "Microphone", RunMethod.Cable, runs: true);
        inputs.Items.Add(mic);
        outputs.Summary = "Master + 4 programs";
        inputs.Summary = "1 device";

        var title = new PageTitleLine
        {
            Title = "Volume",
            Middle = Pills(("Easy", false), ("Advanced", true)),
            Right = new Button { Classes = { "pill" }, Content = "Restore defaults", Tag = "grouptool" },
        };
        var tabs = new PageTabs("Mixer", "Wiring");
        var tabRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        tabRow.Children.Add(new PageTabsView { DataContext = tabs });
        var presets = new PresetPicker { DataContext = new ChoiceItem("Volume presets", new[] { "Custom" }, "Custom", _ => { }) };
        Grid.SetColumn(presets, 1);
        tabRow.Children.Add(presets);
        var cards = new StackPanel { Spacing = 6, [Grid.IsSharedSizeScopeProperty] = true };
        cards.Children.Add(tabRow);
        cards.Children.Add(GroupOf(outputs));
        cards.Children.Add(GroupOf(inputs));
        var page = new StackPanel { Spacing = 6, Margin = new Thickness(12, 12, 12, 24), Children = { title, cards } };
        camera.Sized(new ScrollViewer { Content = page }, "volume", Camera.PageWidth, Camera.PageHeight, crop: true);
    }

    private static void Fill(VolumeRow row, string playsOn, RunMethod method, bool runs)
    {
        row.RouteText = playsOn;
        row.Method = MethodChip.Of(method);
        row.FxRuns = runs;
        row.Fact = method == RunMethod.None ? string.Empty : EffectsRules.NoDelay;
    }

    private static Control GroupOf(VolumeGroup group)
    {
        var body = new StackPanel { Spacing = 6, DataContext = group };
        body.Children.Add(new MixerHeadings());
        body.Children.Add(new ListBox { Classes = { "rows", "compact" }, ItemsSource = group.Items });
        return new GroupCard { Group = group, Content = body };
    }

    private static Control Pills(params (string Text, bool On)[] pills)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (text, on) in pills)
        {
            var pill = new Button { Content = text, Tag = "grouptool", Classes = { "pill" } };
            pill.Classes.Set("custom", on);
            row.Children.Add(pill);
        }
        return row;
    }
}
