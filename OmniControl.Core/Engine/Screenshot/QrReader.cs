using ZXing;
using ZXing.Common;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>Finds and reads the QR codes in a picture, on this PC only (ZXing.Net, Apache-2.0); nothing is sent anywhere. Slow on a large picture: call it off the UI thread.</summary>
public static class QrReader
{
    /// <summary>The text of every QR code found, each once, in the order found; empty when there is none.</summary>
    public static IReadOnlyList<string> Read(ScreenImage image)
    {
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE } },
        };
        var results = reader.DecodeMultiple(source);
        return results is null
            ? Array.Empty<string>()
            : results.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
    }
}
