using System.Text.Json;
using OmniControl.App.Devices;
using OmniControl.Core.Engine;
using Xunit;

namespace OmniControl.Tests;

/// <summary>The simulation method: step words and checks, reading a uiautomator dump, adb's text answers, touches, and the runner on a made-up phone (no adb, no emulator).</summary>
public sealed class SimulationTests
{
    private static string Dump(bool on) =>
        "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\">" +
        "<node text=\"\" resource-id=\"\" content-desc=\"\" clickable=\"false\" bounds=\"[0,0][1080,1920]\">" +
        "<node text=\"Desk plug\" resource-id=\"app:id/name\" content-desc=\"\" clickable=\"true\" bounds=\"[100,200][500,300]\" />" +
        "<node text=\"Lamp\" resource-id=\"app:id/name\" content-desc=\"\" clickable=\"true\" bounds=\"[100,400][500,500]\" />" +
        "<node text=\"\" resource-id=\"app:id/power\" content-desc=\"Power\" clickable=\"true\" bounds=\"[400,1000][680,1280]\" />" +
        (on ? "<node text=\"On\" resource-id=\"app:id/state\" content-desc=\"\" clickable=\"false\" bounds=\"[400,900][680,960]\" />" : "") +
        "</node></hierarchy>\nUI hierarchy dumped to: /dev/tty";

    /// <summary>A phone whose one screen has a Power button that flips "On" on and off.</summary>
    private sealed class FakePhone : IAdbScreen
    {
        public bool On;
        public int Taps;

        public Task<AdbResult> DumpAsync() => Task.FromResult(new AdbResult(true, Dump(On)));

        public Task<AdbResult> TapAsync(int x, int y)
        {
            Taps++;
            if (x is >= 400 and < 680 && y is >= 1000 and < 1280) On = !On;
            return Ok();
        }

        public Task<AdbResult> SwipeAsync(int fromX, int fromY, int toX, int toY) => Ok();
        public Task<AdbResult> TypeAsync(string text) => Ok();
        public Task<AdbResult> BackAsync() => Ok();
        public Task<AdbResult> OpenAppAsync(string package) => Ok();
        public Task<AdbResult> SizeAsync() => Task.FromResult(new AdbResult(true, "Physical size: 1080x1920"));
        private static Task<AdbResult> Ok() => Task.FromResult(new AdbResult(true, string.Empty));
    }

    private static readonly SimTiming Quick = new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5), TimeSpan.Zero);

    private static List<SimStep> Recording() => new()
    {
        new SimStep { Kind = SimStepKind.OpenApp, Package = "com.tplink.iot" },
        new SimStep { Kind = SimStepKind.Tap, Text = "Desk plug" },
        new SimStep { Kind = SimStepKind.Tap, Description = "Power" },
        new SimStep { Kind = SimStepKind.Check, Text = "On" },
    };

    [Fact]
    public void A_dump_with_text_around_it_is_read_and_elements_are_found_by_text_description_and_id()
    {
        var screen = SimScreen.Parse(Dump(true))!;
        Assert.Equal(5, screen.Nodes.Count);
        Assert.Equal(300, screen.Find(new SimStep { Kind = SimStepKind.Tap, Text = "desk PLUG" })!.CenterX);
        Assert.Equal(1140, screen.Find(new SimStep { Kind = SimStepKind.Tap, Description = "Power" })!.CenterY);
        Assert.Equal(450, screen.Find(new SimStep { Kind = SimStepKind.Tap, Text = "Lamp", ResourceId = "app:id/name" })!.CenterY);
        Assert.Null(screen.Find(new SimStep { Kind = SimStepKind.Tap, Text = "Fan" }));
        Assert.True(screen.Shows("On"));
        Assert.False(SimScreen.Parse(Dump(false))!.Shows("On"));
        Assert.Null(SimScreen.Parse("nothing here"));
        Assert.Null(SimScreen.Parse("<hierarchy><node"));
    }

    [Fact]
    public void A_tap_becomes_a_step_on_the_element_under_it_and_a_blank_spot_keeps_its_position()
    {
        var screen = SimScreen.Parse(Dump(false))!;
        var step = screen.TapAt(300, 250);
        Assert.Equal("Desk plug", step.Text);
        Assert.Equal("app:id/name", step.ResourceId);
        var icon = screen.TapAt(500, 1100);
        Assert.Equal("Power", icon.Description);
        var blank = screen.TapAt(10, 1800);
        Assert.False(blank.HasIdentity);
        Assert.Equal((10, 1800), (blank.X, blank.Y));
    }

    [Fact]
    public void Steps_say_what_they_do_and_what_they_still_need()
    {
        var tap = new SimStep { Kind = SimStepKind.Tap, Text = "Power" };
        Assert.Equal(new SimStepWords("Wait for", "\"Power\"", "· tap it"), SimSteps.Words(tap));
        Assert.Equal("Open app", SimSteps.Words(new SimStep { Kind = SimStepKind.OpenApp, Package = "a.b" }).Lead);
        Assert.NotNull(SimSteps.Missing(SimSteps.Blank(SimStepKind.Tap)));
        Assert.NotNull(SimSteps.Missing(SimSteps.Blank(SimStepKind.Check)));
        Assert.NotNull(SimSteps.Missing(new SimStep { Kind = SimStepKind.OpenApp, Package = "rm -rf /" }));
        Assert.Null(SimSteps.Missing(SimSteps.Blank(SimStepKind.Swipe)));
        Assert.Null(SimSteps.Missing(SimSteps.Blank(SimStepKind.Back)));
        Assert.Equal(7, SimSteps.All.Count);
    }

    [Fact]
    public void Adb_text_is_read_and_typed_text_is_quoted()
    {
        Assert.Equal("com.tplink.iot", AdbClient.CurrentPackageOf("  mCurrentFocus=Window{4c2e u0 com.tplink.iot/com.tplink.iot.MainActivity}\n"));
        Assert.Equal(string.Empty, AdbClient.CurrentPackageOf("mCurrentFocus=null"));
        Assert.Equal((1080, 1920), AdbClient.SizeOf("Physical size: 1080x1920"));
        Assert.Null(AdbClient.SizeOf("error"));
        Assert.Equal("my%slamp'\\''s", AdbClient.EscapeForInput("my lamp's"));
    }

    [Fact]
    public void Touches_are_scaled_to_the_screen_and_told_apart_as_taps_and_swipes()
    {
        Assert.Equal((32767, 32767), TouchReader.RangesOf("  ABS_MT_POSITION_X : value 0, min 0, max 32767, fuzz 0\n  ABS_MT_POSITION_Y : value 0, min 0, max 32767, fuzz 0"));
        var reader = new TouchReader(32767, 32767, 1080, 1920);
        Assert.Null(reader.Feed("/dev/input/event3: EV_ABS       ABS_MT_TRACKING_ID   00000001"));
        Assert.Null(reader.Feed("/dev/input/event3: EV_ABS       ABS_MT_POSITION_X    00004000"));
        Assert.Null(reader.Feed("/dev/input/event3: EV_ABS       ABS_MT_POSITION_Y    00004000"));
        Assert.Null(reader.Feed("/dev/input/event3: EV_KEY       BTN_TOUCH            DOWN"));
        var tap = reader.Feed("/dev/input/event3: EV_KEY       BTN_TOUCH            UP");
        Assert.NotNull(tap);
        Assert.True(tap!.IsTap);
        Assert.Equal((540, 960), (tap.ToX, tap.ToY));
        Assert.Null(reader.Feed("/dev/input/event3: EV_ABS       ABS_MT_TRACKING_ID   ffffffff"));

        reader.Feed("EV_ABS ABS_MT_TRACKING_ID 00000002");
        reader.Feed("EV_ABS ABS_MT_POSITION_X 00004000");
        reader.Feed("EV_ABS ABS_MT_POSITION_Y 00006000");
        reader.Feed("EV_ABS ABS_MT_POSITION_Y 00002000");
        var swipe = reader.Feed("EV_ABS ABS_MT_TRACKING_ID ffffffff");
        Assert.False(swipe!.IsTap);
        Assert.Equal("up", swipe.Direction);
    }

    [Fact]
    public async Task Toggle_taps_and_On_or_Off_leave_a_device_that_is_already_there_alone()
    {
        var phone = new FakePhone();
        var steps = Recording();

        var turnedOn = await SimRunner.RunAsync(phone, steps, SimGoal.On, Quick);
        Assert.True(turnedOn.Ok);
        Assert.True(phone.On);
        Assert.Equal(true, turnedOn.IsOn);
        var taps = phone.Taps;

        var again = await SimRunner.RunAsync(phone, steps, SimGoal.On, Quick);
        Assert.True(again.Ok);
        Assert.True(again.Skipped);
        Assert.True(phone.On);
        Assert.Equal(taps + 1, phone.Taps); // only the tap that opens the device: the switch was left alone

        var toggled = await SimRunner.RunAsync(phone, steps, SimGoal.Toggle, Quick);
        Assert.False(phone.On);
        Assert.Equal(false, toggled.IsOn);
        Assert.Equal(taps + 3, phone.Taps); // Desk plug, then Desk plug and Power

        var off = await SimRunner.RunAsync(phone, steps, SimGoal.Off, Quick);
        Assert.True(off.Skipped);
    }

    [Fact]
    public async Task A_failing_step_stops_the_run_and_says_which_one_and_why()
    {
        var phone = new FakePhone();
        var steps = Recording();
        steps[1].Text = "Fan";
        var result = await SimRunner.RunAsync(phone, steps, SimGoal.On, Quick);
        Assert.False(result.Ok);
        Assert.Equal(1, result.FailedIndex);
        Assert.Contains("\"Fan\"", result.Problem);
        Assert.Equal(0, phone.Taps);

        var unfinished = await SimRunner.TestStepAsync(phone, SimSteps.Blank(SimStepKind.Tap), 4, Quick);
        Assert.Equal(4, unfinished.FailedIndex);
        var check = await SimRunner.TestStepAsync(phone, new SimStep { Kind = SimStepKind.Check, Text = "On" }, 3, Quick);
        Assert.False(check.Ok);
    }

    [Fact]
    public void Recorded_steps_are_saved_with_their_kind_as_a_word_and_come_back()
    {
        var json = JsonSerializer.Serialize(new DeviceSettings { Devices = { new DeviceRecord { Name = "Desk plug", Method = DeviceMethods.Simulation, Steps = Recording() } } });
        Assert.Contains("\"Tap\"", json);
        Assert.DoesNotContain("assword", json);
        var back = JsonSerializer.Deserialize<DeviceSettings>(json)!.Devices[0];
        Assert.Equal(SimStepKind.Check, back.Steps[^1].Kind);
        Assert.Equal("Power", back.Steps[2].Description);
    }

    [Fact]
    public void The_emulators_are_one_list_with_official_pages_and_a_method_that_is_not_known_falls_back()
    {
        Assert.All(Emulators.All, e => Assert.StartsWith("https://", e.DownloadPage));
        Assert.Equal(Emulators.All.Count, Emulators.All.Select(e => e.Id).Distinct().Count());
        Assert.Equal(DeviceMethods.Simulation, DeviceMethods.Known("Simulation"));
        Assert.Equal(DeviceMethods.Scripts, DeviceMethods.Known("Teleport"));
    }
}
