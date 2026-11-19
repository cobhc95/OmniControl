using System.Buffers.Binary;
using System.IO.Compression;
using OmniControl.Core;
using StateTarget = OmniControl.Core.Engine.StateTarget;

namespace OmniControl.Tests;

/// Reading a simulated device's on/off state on made-up screens (no adb, no emulator): a Tapo-like card whose power button only changes
/// colour, a Kasa-like card whose words and switch change, a card that moved down the list under another one that looks the same, and the
/// On / Off / Toggle run that taps only when the reading says so.
public sealed class StateDetectionTests
{
    // Dumps are in the phone's pixels (1080 x 1920); screenshots are half that, as some emulators scale them.
    private const int ScreenW = 1080, ScreenH = 1920, Scale = 2;

    private static readonly (byte R, byte G, byte B) Background = (30, 30, 30), CardGrey = (42, 42, 42), Teal = (0, 170, 200), OffGrey = (110, 110, 110);

    private sealed record Card(string Name, int Top, bool On, bool KasaStyle);

    private static string Dump(params Card[] cards)
    {
        var body = string.Concat(cards.Select(c =>
        {
            var t = c.Top;
            var inner = $"<node class=\"android.widget.TextView\" text=\"{c.Name}\" resource-id=\"app:id/name\" content-desc=\"\" clickable=\"false\" bounds=\"[80,{t + 40}][600,{t + 100}]\" />";
            inner += c.KasaStyle
                ? $"<node class=\"android.widget.TextView\" text=\"{(c.On ? "On" : "Off")}\" resource-id=\"app:id/state\" content-desc=\"\" clickable=\"false\" bounds=\"[80,{t + 120}][300,{t + 170}]\" />" +
                  $"<node class=\"android.widget.Switch\" text=\"\" resource-id=\"com.kasa:id/toggle\" content-desc=\"{(c.On ? "Turn off" : "Turn on")}\" clickable=\"true\" checkable=\"true\" checked=\"{(c.On ? "true" : "false")}\" bounds=\"[840,{t + 90}][980,{t + 230}]\" />"
                : $"<node class=\"android.widget.ImageView\" text=\"\" resource-id=\"com.tplink.iot:id/power\" content-desc=\"\" clickable=\"true\" bounds=\"[840,{t + 90}][980,{t + 230}]\" />";
            return $"<node class=\"android.view.ViewGroup\" text=\"\" resource-id=\"app:id/card\" content-desc=\"\" clickable=\"true\" bounds=\"[40,{t}][1040,{t + 300}]\">{inner}</node>";
        }));
        return "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\">" +
               $"<node class=\"android.widget.FrameLayout\" text=\"\" resource-id=\"\" content-desc=\"\" clickable=\"false\" bounds=\"[0,0][{ScreenW},{ScreenH}]\">" +
               $"<node class=\"androidx.recyclerview.widget.RecyclerView\" text=\"\" resource-id=\"app:id/list\" content-desc=\"\" clickable=\"false\" bounds=\"[0,200][{ScreenW},1900]\">{body}</node>" +
               "</node></hierarchy>";
    }

    /// The screenshot: each card grey, its power button coloured while on (Tapo-like) or grey (Kasa-like, whose colour never changes).
    private static RgbaImage Picture(Card[] cards, (byte R, byte G, byte B)? background = null)
    {
        int w = ScreenW / Scale, h = ScreenH / Scale;
        var pixels = new byte[w * h * 4];
        void Fill(int l, int t, int r, int b, (byte R, byte G, byte B) c)
        {
            for (var y = t / Scale; y < b / Scale; y++)
                for (var x = l / Scale; x < r / Scale; x++)
                {
                    var i = (y * w + x) * 4;
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (c.R, c.G, c.B, 255);
                }
        }
        Fill(0, 0, ScreenW, ScreenH, background ?? Background);
        foreach (var c in cards)
        {
            Fill(40, c.Top, 1040, c.Top + 300, CardGrey);
            Fill(840, c.Top + 90, 980, c.Top + 230, c.On && !c.KasaStyle ? Teal : OffGrey);
        }
        return new RgbaImage(w, h, pixels);
    }

    private static StateSample Look(Card[] cards, SimStep step, bool withPicture = true, (byte R, byte G, byte B)? background = null)
    {
        var screen = SimScreen.Parse(Dump(cards))!;
        var node = screen.Find(step) ?? throw new InvalidOperationException("no switch");
        return StateSample.Take(screen, node, withPicture ? Png.Decode(Encode(Picture(cards, background))) : null);
    }

    private static readonly SimStep TapoPower = new() { Kind = SimStepKind.Tap, ResourceId = "com.tplink.iot:id/power", Anchor = "Tapo L530" };

    private static StateModel Taught(Func<bool, StateSample> look)
    {
        var model = new StateModel();
        for (var i = 0; i < 3; i++)
        {
            model.OnSamples.Add(look(true));
            model.OffSamples.Add(look(false));
        }
        Assert.Null(model.Derive());
        return model;
    }

    [Fact]
    public void A_button_that_only_changes_colour_is_read_by_its_colour_whatever_the_theme()
    {
        Card[] Screen(bool on) => new[] { new Card("Bedroom lamp", 300, false, false), new Card("Tapo L530", 700, on, false) };
        var model = Taught(on => Look(Screen(on), TapoPower));

        Assert.Contains("colour", model.Summary);
        Assert.Equal(DeviceState.On, model.Read(Look(Screen(true), TapoPower)).State);
        Assert.Equal(DeviceState.Off, model.Read(Look(Screen(false), TapoPower)).State);
        Assert.Equal(DeviceState.On, model.Read(Look(Screen(true), TapoPower, background: (235, 235, 235))).State); // a light theme around it
        var blind = model.Read(Look(Screen(true), TapoPower, withPicture: false)); // no screenshot: colour cannot vote
        Assert.Equal(DeviceState.Unknown, blind.State);
        Assert.True(blind.Confidence < CoreTiming.StateReadThreshold);
    }

    [Fact]
    public void A_card_whose_words_and_switch_change_is_read_without_any_colour()
    {
        Card[] Screen(bool on) => new[] { new Card("Desk lamp", 500, on, true) };
        StateSample LookAt(bool on)
        {
            var screen = SimScreen.Parse(Dump(Screen(on)))!;
            var node = new StateTarget { ResourceId = "com.kasa:id/toggle" }.Locate(screen)!;
            return StateSample.Take(screen, node, Png.Decode(Encode(Picture(Screen(on)))));
        }
        var model = Taught(LookAt);

        Assert.Contains("switch state", model.Summary);
        Assert.Contains("words", model.Summary);
        Assert.Equal(string.Empty, model.Target!.Description); // "Turn off" / "Turn on" change with the state, so they never find it
        Assert.Equal("Desk lamp", model.Target.Anchor);
        Assert.Equal(DeviceState.On, model.Read(LookAt(true)).State);
        var off = model.Read(LookAt(false));
        Assert.Equal(DeviceState.Off, off.State);
        Assert.True(off.Confidence > 0.9);
    }

    [Fact]
    public void A_card_that_moved_is_found_by_its_name_not_by_the_look_alike_switch_above_it()
    {
        Card[] Screen(bool on) => new[] { new Card("Bedroom lamp", 300, false, false), new Card("Tapo L530", 700, on, false) };
        var model = Taught(on => Look(Screen(on), TapoPower));
        var scrolled = SimScreen.Parse(Dump(new Card("Tapo L530", 300, true, false), new Card("Bedroom lamp", 700, false, false)))!;
        var found = model.Target!.Locate(scrolled)!;
        Assert.Equal(300 + 160, found.CenterY);
        Assert.Null(model.Target.Locate(SimScreen.Parse(Dump(new Card("Bedroom lamp", 300, false, false)))!)); // not on the screen: scroll and look again
    }

    [Fact]
    public void On_and_off_that_look_the_same_are_refused()
    {
        var model = new StateModel();
        var same = Look(new[] { new Card("Tapo L530", 700, true, false) }, TapoPower);
        model.OnSamples.Add(same);
        model.OffSamples.Add(same);
        Assert.NotNull(model.Derive());
        Assert.False(model.IsTaught);
    }

    [Fact]
    public void Unsure_readings_wear_the_confidence_down_until_the_device_asks_to_be_taught_again()
    {
        Card[] Screen(bool on) => new[] { new Card("Tapo L530", 700, on, false) };
        var model = Taught(on => Look(Screen(on), TapoPower));
        Assert.False(model.IsDrifting);
        for (var i = 0; i < 4; i++) model.Note(0.2);
        Assert.True(model.IsDrifting);
    }

    /// A phone showing the two Tapo cards; a tap on the L530's power button switches it; its screenshot can be made to fail.
    private sealed class TapoPhone : IAdbScreen
    {
        public bool On;
        public int Taps;
        public bool Blind;

        private Card[] Cards => new[] { new Card("Bedroom lamp", 300, false, false), new Card("Tapo L530", 700, On, false) };

        public Task<AdbResult> DumpAsync() => Task.FromResult(new AdbResult(true, Dump(Cards)));

        public Task<byte[]?> ScreenshotAsync() => Task.FromResult(Blind ? null : (byte[]?)Encode(Picture(Cards)));

        public Task<AdbResult> TapAsync(int x, int y)
        {
            Taps++;
            if (x is >= 840 and < 980 && y is >= 790 and < 930) On = !On;
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

    [Fact]
    public async Task On_and_Off_tap_only_when_needed_report_only_a_real_change_and_tap_nothing_when_unsure()
    {
        var phone = new TapoPhone { On = true };
        var steps = new List<SimStep> { new() { Kind = SimStepKind.OpenApp, Package = "com.tplink.iot" }, TapoPower.Clone() };
        var model = new StateModel();
        model.OnSamples.AddRange((await SimRunner.CaptureAsync(phone, steps[1], model, 3, TimeSpan.Zero)).Samples);
        phone.On = false;
        model.OffSamples.AddRange((await SimRunner.CaptureAsync(phone, steps[1], model, 3, TimeSpan.Zero)).Samples);
        Assert.Null(model.Derive());

        var off = await SimRunner.RunAsync(phone, steps, model, SimGoal.Off, Quick);
        Assert.True(off.Ok);
        Assert.True(off.Skipped);
        Assert.Equal(0, phone.Taps);

        var on = await SimRunner.RunAsync(phone, steps, model, SimGoal.On, Quick);
        Assert.True(on.Ok);
        Assert.Equal(true, on.IsOn);
        Assert.Equal(1, phone.Taps);

        var toggled = await SimRunner.RunAsync(phone, steps, model, SimGoal.Toggle, Quick);
        Assert.Equal(false, toggled.IsOn);
        Assert.False(phone.On);

        phone.Blind = true;
        var unsure = await SimRunner.RunAsync(phone, steps, model, SimGoal.On, Quick);
        Assert.True(unsure.Unsure);
        Assert.False(unsure.Ok);
        Assert.Equal(2, phone.Taps); // nothing tapped while the state could not be read
    }

    [Fact]
    public void A_png_from_screencap_is_decoded()
    {
        var picture = Picture(new[] { new Card("Tapo L530", 700, true, false) });
        var back = Png.Decode(Encode(picture))!;
        Assert.Equal((picture.Width, picture.Height), (back.Width, back.Height));
        Assert.Equal(Teal, back.At(910 / Scale, 860 / Scale));
        Assert.Null(Png.Decode(new byte[] { 1, 2, 3 }));
    }

    /// An unfiltered RGBA PNG (what Png.Decode reads; the CRCs are left at zero because it does not check them).
    internal static byte[] Encode(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)image.Height);
        (header[8], header[9]) = (8, 6);
        Chunk(output, "IHDR", header);
        using (var packed = new MemoryStream())
        {
            using (var deflate = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
                for (var y = 0; y < image.Height; y++)
                {
                    deflate.WriteByte(0);
                    deflate.Write(image.Pixels, y * image.Width * 4, image.Width * 4);
                }
            Chunk(output, "IDAT", packed.ToArray());
        }
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] body)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        output.Write(length);
        output.Write(System.Text.Encoding.ASCII.GetBytes(type));
        output.Write(body);
        output.Write(new byte[4]);
    }
}
