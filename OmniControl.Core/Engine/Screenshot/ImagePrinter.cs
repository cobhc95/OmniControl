using static OmniControl.Core.Native.PrintNative;

namespace OmniControl.Core.Engine.Screenshot;

/// How a print ended.
public enum PrintOutcome { Printed, Cancelled, Failed }

/// Prints one picture: Windows' own print dialog chooses the printer and copies, then the picture is printed centred on one page at its
/// screen size (96 pixels per inch), made smaller only when it does not fit. Blocks until the dialog closes; call it on the UI thread.
/// The printer's device context and the dialog's printer settings are released before it returns.
public static class ImagePrinter
{
    private const double ScreenPixelsPerInch = 96;

    public static PrintOutcome Print(ScreenImage image, string documentName, IntPtr owner)
    {
        var dialog = new PrintDlg
        {
            StructSize = Marshal.SizeOf<PrintDlg>(),
            Owner = owner,
            Flags = PdReturnDc | PdNoSelection | PdNoPageNums | PdUseDevModeCopiesAndCollate,
            Copies = 1,
        };
        try
        {
            if (!ShowPrintDlg(ref dialog)) return CommDlgExtendedError() == 0 ? PrintOutcome.Cancelled : PrintOutcome.Failed;
            return dialog.Hdc == IntPtr.Zero ? PrintOutcome.Failed : PrintOn(dialog.Hdc, image, documentName);
        }
        finally
        {
            if (dialog.Hdc != IntPtr.Zero) ScreenshotNative.DeleteDC(dialog.Hdc);
            if (dialog.DevMode != IntPtr.Zero) ScreenshotNative.GlobalFree(dialog.DevMode);
            if (dialog.DevNames != IntPtr.Zero) ScreenshotNative.GlobalFree(dialog.DevNames);
        }
    }

    private static PrintOutcome PrintOn(IntPtr hdc, ScreenImage image, string documentName)
    {
        var doc = new DocInfo { Size = Marshal.SizeOf<DocInfo>(), DocName = documentName };
        if (StartDoc(hdc, ref doc) <= 0) return PrintOutcome.Failed;
        if (StartPage(hdc) <= 0)
        {
            AbortDoc(hdc);
            return PrintOutcome.Failed;
        }
        var place = Fit(image.Width, image.Height, GetDeviceCaps(hdc, HorzRes), GetDeviceCaps(hdc, VertRes),
            GetDeviceCaps(hdc, LogPixelsX), GetDeviceCaps(hdc, LogPixelsY));
        var header = new ScreenshotNative.BitmapInfoHeader
        {
            Size = Marshal.SizeOf<ScreenshotNative.BitmapInfoHeader>(), Width = image.Width, Height = -image.Height, Planes = 1, BitCount = 32,
        };
        SetStretchBltMode(hdc, Halftone);
        var drawn = StretchDIBits(hdc, place.X, place.Y, place.Width, place.Height, 0, 0, image.Width, image.Height,
            image.Pixels, ref header, ScreenshotNative.DibRgbColors, ScreenshotNative.SrcCopy) > 0;
        if (EndPage(hdc) <= 0 || !drawn)
        {
            AbortDoc(hdc);
            return PrintOutcome.Failed;
        }
        return EndDoc(hdc) > 0 ? PrintOutcome.Printed : PrintOutcome.Failed;
    }

    /// Where the picture goes on the page, in printer dots: its screen size at the printer's resolution, shrunk to fit, centred.
    internal static ScreenRect Fit(int imageWidth, int imageHeight, int pageWidth, int pageHeight, int dotsPerInchX, int dotsPerInchY)
    {
        var width = imageWidth * dotsPerInchX / ScreenPixelsPerInch;
        var height = imageHeight * dotsPerInchY / ScreenPixelsPerInch;
        var shrink = Math.Min(1, Math.Min(pageWidth / width, pageHeight / height));
        var w = Math.Max(1, (int)Math.Round(width * shrink));
        var h = Math.Max(1, (int)Math.Round(height * shrink));
        return new ScreenRect((pageWidth - w) / 2, (pageHeight - h) / 2, w, h);
    }
}
