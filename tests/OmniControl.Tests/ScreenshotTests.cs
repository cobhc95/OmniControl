using OmniControl.App.Features.Screenshot;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.Tests;

/// The screenshot feature's pure parts: the file name pattern, the formats and their encoding, the selection maths and bar placement, the
/// undo list, the blur's pixelation, the print fit, the settings switch and which QR texts may be opened. Nothing is captured or shown.
public class ScreenshotTests
{
    private static readonly DateTime At = new(2026, 11, 16, 9, 5, 7);
    private static readonly ScreenRect Picture = new(0, 0, 1000, 800);

    private static ScreenImage Solid(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255; }
        return new ScreenImage(width, height, pixels);
    }

    // ---- File names ----

    [Fact]
    public void Pattern_FillsDateTimeAndApp() =>
        Assert.Equal("Screenshot 2026-11-16 09-05-07 chrome", FileNamePattern.Make("Screenshot {date} {time} {app}", At, "chrome"));

    [Fact]
    public void Pattern_EmptyAppLeavesNoDoubleSpace() =>
        Assert.Equal("Shot 2026-11-16", FileNamePattern.Make("Shot {app} {date}", At, ""));

    [Fact]
    public void Pattern_DropsCharactersWindowsRefuses() =>
        Assert.Equal("ab", FileNamePattern.Make("a:<>|b", At, ""));

    [Fact]
    public void Pattern_NothingLeftFallsBack() => Assert.Equal("Screenshot", FileNamePattern.Make("{app}", At, ""));

    [Fact]
    public void UniquePath_NumbersTakenNames()
    {
        var taken = new HashSet<string> { Path.Combine("C:\\x", "a.png"), Path.Combine("C:\\x", "a (2).png") };
        Assert.Equal(Path.Combine("C:\\x", "a (3).png"), FileNamePattern.UniquePath("C:\\x", "a", ".png", taken.Contains));
    }

    // ---- Formats and encoding ----

    [Theory]
    [InlineData(ImageFormat.Png, ".png", false)]
    [InlineData(ImageFormat.Jpg, ".jpg", true)]
    [InlineData(ImageFormat.WebP, ".webp", true)]
    [InlineData(ImageFormat.Bmp, ".bmp", false)]
    public void Formats_EndingAndQuality(ImageFormat format, string extension, bool quality)
    {
        Assert.Equal(extension, ImageFormats.Extension(format));
        Assert.Equal(quality, ImageFormats.UsesQuality(format));
        Assert.Equal(format, ImageFormats.Parse(ImageFormats.Name(format)));
    }

    [Theory]
    [InlineData(ImageFormat.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(ImageFormat.Jpg, new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData(ImageFormat.WebP, new byte[] { 0x52, 0x49, 0x46, 0x46 })]
    [InlineData(ImageFormat.Bmp, new byte[] { 0x42, 0x4D })]
    public void Encode_WritesTheFormatsSignature(ImageFormat format, byte[] signature)
    {
        var bytes = ImageEncoder.Encode(Solid(7, 5, 10, 20, 30), format, 90);
        Assert.Equal(signature, bytes.Take(signature.Length).ToArray());
    }

    [Fact]
    public void Bmp_IsBottomUpAndPadded()
    {
        var image = new ScreenImage(1, 2, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 });
        var bmp = ImageEncoder.Bmp(image);
        Assert.Equal(54 + 2 * 4, bmp.Length); // one 3-byte pixel per row, padded to 4
        Assert.Equal(new byte[] { 4, 5, 6 }, bmp.Skip(54).Take(3).ToArray()); // the bottom row first
        Assert.Equal(new byte[] { 1, 2, 3 }, bmp.Skip(58).Take(3).ToArray());
    }

    [Fact]
    public void Jpg_LowerQualityIsSmaller()
    {
        var rnd = new Random(1);
        var pixels = new byte[64 * 64 * 4];
        rnd.NextBytes(pixels);
        var image = new ScreenImage(64, 64, pixels);
        Assert.True(ImageEncoder.Encode(image, ImageFormat.Jpg, 20).Length < ImageEncoder.Encode(image, ImageFormat.Jpg, 95).Length);
    }

    // ---- Selection ----

    [Fact]
    public void Drag_AnyDirectionGivesTheSameRectangle()
    {
        var expected = new ScreenRect(10, 20, 90, 60);
        Assert.Equal(expected, SelectionMath.FromDrag(10, 20, 100, 80, Picture));
        Assert.Equal(expected, SelectionMath.FromDrag(100, 80, 10, 20, Picture));
    }

    [Fact]
    public void Drag_IsHeldToThePicture() =>
        Assert.Equal(new ScreenRect(900, 0, 100, 50), SelectionMath.FromDrag(900, -30, 1200, 50, Picture));

    [Theory]
    [InlineData(100, 100, Grip.NW)]
    [InlineData(150, 100, Grip.N)]
    [InlineData(200, 150, Grip.E)]
    [InlineData(203, 198, Grip.SE)]
    [InlineData(150, 150, Grip.Move)]
    [InlineData(400, 400, Grip.None)]
    public void HitTest_FindsHandles(int x, int y, Grip grip) =>
        Assert.Equal(grip, SelectionMath.HitTest(new ScreenRect(100, 100, 100, 100), x, y, 4));

    [Fact]
    public void Resize_PastTheOtherEdgeTurnsOver() =>
        Assert.Equal(new ScreenRect(50, 100, 50, 100), SelectionMath.Resize(new ScreenRect(100, 100, 100, 100), Grip.E, -150, 0, Picture));

    [Fact]
    public void Move_StaysWholeInsideThePicture() =>
        Assert.Equal(new ScreenRect(900, 0, 100, 100), SelectionMath.Move(new ScreenRect(500, 50, 100, 100), 800, -500, Picture));

    [Fact]
    public void Nudge_ShiftMovesTenPixels()
    {
        var sel = new ScreenRect(100, 100, 50, 50);
        Assert.Equal(sel with { X = 101 }, SelectionMath.Nudge(sel, 1, 0, false, Picture));
        Assert.Equal(sel with { Y = 90 }, SelectionMath.Nudge(sel, 0, -1, true, Picture));
    }

    [Fact]
    public void Toolbar_GoesRightThenLeftThenInside()
    {
        Assert.Equal((116.0, 10.0), SelectionMath.PlaceBar(BarSide.Right, 10, 10, 100, 100, 30, 200, 1000, 800, 6));
        Assert.Equal((864.0, 10.0), SelectionMath.PlaceBar(BarSide.Right, 900, 10, 95, 100, 30, 200, 1000, 800, 6));
        Assert.Equal((964.0, 10.0), SelectionMath.PlaceBar(BarSide.Right, 0, 10, 1000, 100, 30, 200, 1000, 800, 6));
    }

    [Fact]
    public void ActionBar_GoesBelowThenAbove() =>
        Assert.Equal((60.0, 664.0), SelectionMath.PlaceBar(BarSide.Below, 10, 700, 100, 100, 50, 30, 1000, 800, 6));

    // ---- Undo, blur, print, settings, links ----

    [Fact]
    public void Undo_RedoAndDropAfterNewStep()
    {
        var list = new UndoList<string>();
        var dropped = new List<string>();
        list.Dropped += dropped.Add;
        list.Add("a");
        list.Add("b");
        Assert.True(list.Undo());
        Assert.Equal(new[] { "a" }, list.Items);
        Assert.True(list.Redo());
        Assert.Equal(new[] { "a", "b" }, list.Items);
        list.Undo();
        list.Add("c");
        Assert.Equal(new[] { "b" }, dropped);
        Assert.False(list.Redo());
    }

    [Fact]
    public void Pixelation_AveragesEachSquare()
    {
        var image = new ScreenImage(2, 1, new byte[] { 0, 0, 0, 255, 200, 100, 50, 255 });
        var coarse = Pixelation.Blocks(image, image.Bounds, 2);
        Assert.Equal(1, coarse.Width);
        Assert.Equal(new byte[] { 100, 50, 25, 255 }, coarse.Pixels);
    }

    [Fact]
    public void Print_ShrinksToFitAndCentres()
    {
        Assert.Equal(new ScreenRect(450, 600, 600, 300), ImagePrinter.Fit(96, 48, 1500, 1500, 600, 600)); // screen size at 600 dpi
        Assert.Equal(new ScreenRect(0, 250, 1000, 500), ImagePrinter.Fit(960, 480, 1000, 1000, 600, 600)); // too big: shrunk
    }

    [Fact]
    public void Settings_SwitchOffStartsOwnCopy()
    {
        var settings = new ScreenshotSettings();
        settings.Shared.Quality = 70;
        settings.SetShared("screenshot.area", false);
        ScreenshotFields.Apply(settings.For("screenshot.area"), ScreenshotFields.Quality, "40");
        Assert.Equal(70, settings.Shared.Quality);
        Assert.Equal(40, settings.For("screenshot.area").Quality);
        settings.SetShared("screenshot.area", true);
        Assert.Equal(70, settings.For("screenshot.area").Quality);
    }

    [Fact]
    public void Defaults_AreJpgAt80AndAskWhereToSave()
    {
        var options = new ScreenshotOptions();
        Assert.Equal(ImageFormat.Jpg, options.Format);
        Assert.Equal(80, options.Quality);
        Assert.True(options.AskFolder);
        Assert.True(options.AskVideoFolder);
        var format = ScreenshotFields.Options(ShotKind.Area, false, () => null).Single(o => o.Key == ScreenshotFields.Format);
        Assert.Equal("JPG", format.Default); // what Restore defaults puts back
        Assert.Contains("PNG", format.Choices!);
    }

    [Fact]
    public void AlwaysSaveHere_WritesTheActionsOwnFolderAndTurnsAskingOff()
    {
        var options = new ScreenshotOptions();
        SaveKinds.AlwaysSaveHere(SaveKind.Recording, options, @"D:\Clips");
        Assert.Equal(@"D:\Clips", options.VideoFolderInUse);
        Assert.False(options.AskVideoFolder);
        Assert.True(options.AskFolder); // pictures keep their own choice
        Assert.Equal("0", ScreenshotFields.Read(options, true, false)[ScreenshotFields.AskVideoFolder]);
    }

    [Theory]
    [InlineData("Clip 1.mp4", "Clip 1")]
    [InlineData("Clip 1", "Clip 1")]
    [InlineData("a<b>:c.mp4", "abc")]
    [InlineData("  .mp4 ", "")]
    public void SaveDialog_NameKeepsTheFormatsEnding(string typed, string clean)
    {
        var model = new SaveFileModel("Save recording", "x", ".mp4", @"C:\Videos") { Name = typed };
        Assert.Equal(clean, model.CleanName);
        Assert.Equal(clean.Length > 0, model.CanSave);
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("WIFI:S:home;T:WPA;P:secret;;", false)]
    [InlineData("just text", false)]
    public void QrLinks_OnlyWebAddressesOpen(string text, bool opens) => Assert.Equal(opens, ScreenshotService.IsWebLink(text));
}
