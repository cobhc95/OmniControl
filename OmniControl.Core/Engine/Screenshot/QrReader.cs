using ZXing;
using ZXing.Common;

namespace OmniControl.Core.Engine.Screenshot;

/// One code found in a picture: what kind it is ("Aztec code") and the text inside.
public sealed record ScannedCode(string Kind, string Text);

/// Finds and reads the 2D codes and 1D barcodes in a picture, on this PC only (ZXing.Net, Apache-2.0); nothing is sent anywhere. Slow on a large picture: call it off the UI thread.
public static class QrReader
{
    /// A crop smaller than this on both sides is retried once enlarged: modules of a tiny code are too few pixels for the finder patterns.
    internal const int SmallSide = 300;
    internal const int Upscale = 3;
    /// Bound on the enlarged picture, the same limit the icon and PNG readers use.
    internal const long MaxPixels = 16L << 20;
    private const int PadSide = 24;

    private static readonly BarcodeFormat[] Formats =
    {
        BarcodeFormat.QR_CODE, BarcodeFormat.AZTEC, BarcodeFormat.DATA_MATRIX, BarcodeFormat.PDF_417, BarcodeFormat.MAXICODE,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93, BarcodeFormat.ITF, BarcodeFormat.CODABAR,
    };

    /// Every code found, each text once, in the order found; empty when there is none.
    public static IReadOnlyList<ScannedCode> Read(ScreenImage image)
    {
        var found = Decode(image);
        if (found.Count == 0 && image.Width < SmallSide && image.Height < SmallSide)
        {
            var big = Enlarged(image);
            if (big is not null) found = Decode(big);
        }
        return found;
    }

    private static List<ScannedCode> Decode(ScreenImage image)
    {
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = Formats },
        };
        var results = reader.DecodeMultiple(source) ?? (reader.Decode(source) is { } one ? new[] { one } : Array.Empty<Result>());
        var seen = new HashSet<string>();
        return results.Where(r => !string.IsNullOrEmpty(r.Text) && seen.Add(r.Text)).Select(r => new ScannedCode(KindName(r.BarcodeFormat), r.Text)).ToList();
    }

    /// The picture enlarged with a margin of its corner colour (a tightly cropped code has no quiet zone), or null when that would pass the bound.
    private static ScreenImage? Enlarged(ScreenImage image)
    {
        int w = image.Width * Upscale + 2 * PadSide, h = image.Height * Upscale + 2 * PadSide;
        if ((long)w * h > MaxPixels) return null;
        var src = image.Pixels;
        var dst = new byte[w * h * ScreenImage.BytesPerPixel];
        for (var i = 0; i < w * h; i++) Buffer.BlockCopy(src, 0, dst, i * ScreenImage.BytesPerPixel, ScreenImage.BytesPerPixel);
        for (var y = 0; y < image.Height * Upscale; y++)
        {
            var sy = y / Upscale;
            for (var x = 0; x < image.Width * Upscale; x++)
                Buffer.BlockCopy(src, (sy * image.Width + x / Upscale) * ScreenImage.BytesPerPixel, dst, ((y + PadSide) * w + x + PadSide) * ScreenImage.BytesPerPixel, ScreenImage.BytesPerPixel);
        }
        return new ScreenImage(w, h, dst);
    }

    private static string KindName(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => "QR code",
        BarcodeFormat.AZTEC => "Aztec code",
        BarcodeFormat.DATA_MATRIX => "Data Matrix code",
        BarcodeFormat.PDF_417 => "PDF417 code",
        BarcodeFormat.MAXICODE => "MaxiCode",
        BarcodeFormat.EAN_13 => "EAN-13 barcode",
        BarcodeFormat.EAN_8 => "EAN-8 barcode",
        BarcodeFormat.UPC_A => "UPC-A barcode",
        BarcodeFormat.UPC_E => "UPC-E barcode",
        BarcodeFormat.CODE_128 => "Code 128 barcode",
        BarcodeFormat.CODE_39 => "Code 39 barcode",
        BarcodeFormat.CODE_93 => "Code 93 barcode",
        BarcodeFormat.ITF => "ITF barcode",
        BarcodeFormat.CODABAR => "Codabar barcode",
        _ => "Code",
    };
}
