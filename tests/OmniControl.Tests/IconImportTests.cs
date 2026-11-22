using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniControl.App.Features.Icons;

namespace OmniControl.Tests;

/// A PNG made into a user icon: its size is read from the header first, an absurd one is refused without being decoded, and the rest is
/// decoded straight to the icon's size.
public class IconImportTests
{
    private static byte[] Header(uint width, uint height)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    [Fact]
    public void Header_GivesTheSize_OrNothingForWhatIsNoPng()
    {
        Assert.Equal((300, 150), IconPictures.PngSize(Header(300, 150)));
        Assert.Null(IconPictures.PngSize(Header(0, 10)));
        Assert.Null(IconPictures.PngSize(Header(uint.MaxValue, 10)));
        Assert.Null(IconPictures.PngSize("GIF89a not a png at all, long enough"u8.ToArray()));
        Assert.Null(IconPictures.PngSize(Header(300, 150)[..20]));
    }

    [Theory]
    [InlineData(IconStore.MaxSide + 1u, 10u)]
    [InlineData(10u, 100_000u)]
    [InlineData(5000u, 5000u)] // under the side limit, over the pixel limit
    public void AbsurdSize_IsRefusedBeforeDecoding(uint width, uint height)
    {
        var (png, problem) = IconStore.SmallPng(Header(width, height)); // only a header: decoding it would fail

        Assert.Null(png);
        Assert.Equal("That picture is too large for an icon. Pick a smaller one.", problem);
    }

    [Fact]
    public void LargePicture_IsDecodedToTheIconSize()
    {
        var size = AvaloniaThread.Run(() =>
        {
            var (png, problem) = IconStore.SmallPng(Png(300, 150));
            Assert.Null(problem);
            using var small = new Bitmap(new MemoryStream(Convert.FromBase64String(png!)));
            return small.PixelSize;
        });

        Assert.Equal(new PixelSize(64, 32), size);
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
