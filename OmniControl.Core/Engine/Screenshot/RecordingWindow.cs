using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// The recording bar's window: left out of every screen capture (so the frame and the bar never appear in the video, nor in any other
/// program's capture) and cut down to the parts that are drawn, so clicks everywhere else reach the windows underneath.
/// </summary>
public static class RecordingWindow
{
    /// <summary>Asks Windows to leave the window out of captures. False on a Windows older than 10 (2004), which cannot.</summary>
    public static bool ExcludeFromCapture(IntPtr window) => SetWindowDisplayAffinity(window, WdaExcludeFromCapture);

    /// <summary>Makes only these parts of the window (in its own physical pixels) visible and clickable.</summary>
    public static void KeepOnly(IntPtr window, IEnumerable<ScreenRect> parts)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var part in parts)
        {
            var piece = CreateRectRgn(part.X, part.Y, part.Right, part.Bottom);
            CombineRgn(region, region, piece, RgnOr);
            DeleteObject(piece);
        }
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region); // on success Windows owns the region
    }
}
