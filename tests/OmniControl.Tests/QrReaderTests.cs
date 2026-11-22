using OmniControl.Core.Engine.Screenshot;
using ZXing;
using ZXing.Common;

namespace OmniControl.Tests;

/// The screenshot code reader finds 2D codes and barcodes of every common kind, light-on-dark ones, and tiny ones.
public class QrReaderTests
{
    private static ScreenImage Picture(BarcodeFormat format, string text, int scale, bool inverted = false)
    {
        var matrix = new MultiFormatWriter().encode(text, format, 0, 0);
        const int quiet = 4; // modules; the writers of some kinds leave none
        var (w, h) = ((matrix.Width + 2 * quiet) * scale, (matrix.Height + 2 * quiet) * scale);
        var pixels = new byte[w * h * ScreenImage.BytesPerPixel];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int mx = x / scale - quiet, my = y / scale - quiet;
                var dark = (mx >= 0 && my >= 0 && mx < matrix.Width && my < matrix.Height && matrix[mx, my]) != inverted;
                var i = (y * w + x) * ScreenImage.BytesPerPixel;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = dark ? (byte)0 : (byte)255;
                pixels[i + 3] = 255;
            }
        return new ScreenImage(w, h, pixels);
    }

    [Theory]
    [InlineData(BarcodeFormat.QR_CODE, "https://example.com/a", 4, "QR code")]
    [InlineData(BarcodeFormat.AZTEC, "Aztec 123", 4, "Aztec code")]
    [InlineData(BarcodeFormat.DATA_MATRIX, "Matrix 456", 4, "Data Matrix code")]
    [InlineData(BarcodeFormat.PDF_417, "Pdf 789", 3, "PDF417 code")]
    [InlineData(BarcodeFormat.CODE_128, "ABC-123", 3, "Code 128 barcode")]
    [InlineData(BarcodeFormat.EAN_13, "5901234123457", 3, "EAN-13 barcode")]
    public void Reads_each_kind(BarcodeFormat format, string text, int scale, string kind)
    {
        var found = QrReader.Read(Picture(format, text, scale));
        Assert.Contains(new ScannedCode(kind, text), found);
    }

    [Theory]
    [InlineData(BarcodeFormat.QR_CODE)]
    [InlineData(BarcodeFormat.AZTEC)]
    public void Reads_light_on_dark(BarcodeFormat format)
    {
        var found = QrReader.Read(Picture(format, "inverted", 4, inverted: true));
        Assert.Contains(found, c => c.Text == "inverted");
    }

    [Theory]
    [InlineData(BarcodeFormat.QR_CODE, 1)]
    [InlineData(BarcodeFormat.QR_CODE, 2)]
    [InlineData(BarcodeFormat.AZTEC, 1)]
    public void Reads_a_small_picture(BarcodeFormat format, int scale)
    {
        var image = Picture(format, "small", scale);
        Assert.True(image.Width < QrReader.SmallSide);
        Assert.Contains(QrReader.Read(image), c => c.Text == "small");
    }

    [Fact]
    public void Blank_picture_holds_no_code()
    {
        var pixels = Enumerable.Repeat((byte)255, 50 * 50 * ScreenImage.BytesPerPixel).ToArray();
        Assert.Empty(QrReader.Read(new ScreenImage(50, 50, pixels)));
    }
}
