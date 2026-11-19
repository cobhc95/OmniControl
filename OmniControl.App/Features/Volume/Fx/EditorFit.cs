namespace OmniControl.App.Features.Volume.Fx;

/// The arithmetic of fitting a plug-in's own editor into a window (adapted from TabForge's <c>PluginEditorFitMath</c>, same owner):
/// the window rectangle that shows the whole editor within the screen's working area, which scroll bars an editor larger than its
/// space needs and how far they scroll, and where the plug-in's view goes for a scroll offset. Pure maths, no UI.
/// Units: device-independent pixels (DIPs) unless a name says pixels.
internal static class EditorFit
{
    /// Kept free of the working area when a window grows for an editor (DIPs, total per axis), so it never touches the screen edges.
    public const double WorkAreaMargin = 40;

    /// Rounding between pixels and DIPs never shows a scroll bar.
    public const double BarSlack = 0.5;

    /// How far one mouse-wheel notch over the bars, or one arrow of a bar, scrolls the editor (DIPs).
    public const double ScrollStep = 40;

    /// The smallest window a popped-out editor starts from before it is grown to the editor (DIPs).
    public const double MinPopOutWidth = 160, MinPopOutHeight = 80;

    /// A popped-out OmniControl editor (the equaliser, sliders, a settings list) at 100 %: its starting size and the space around it (DIPs).
    public const double OwnEditorWidth = 560, OwnEditorHeight = 560, PopOutPadding = 12;

    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// The visible part of the editor and how far it can scroll each way.
    public readonly record struct ScrollLayout(double ViewWidth, double ViewHeight, double RangeX, double RangeY);

    /// Editor pixels to DIPs at a display scale (1.5 = 150 %).
    public static (double Width, double Height) ToDip(int widthPx, int heightPx, double scale) => (widthPx / scale, heightPx / scale);

    /// DIPs to whole pixels at a display scale.
    public static (int Width, int Height) ToPixels(double width, double height, double scale) =>
        ((int)Math.Round(width * scale), (int)Math.Round(height * scale));

    /// The window rectangle that shows the whole editor, at most the working area less <see cref="WorkAreaMargin"/>; the top-left is
    /// kept unless the window would then leave the working area. <paramref name="chrome"/> is everything of the window that is not the
    /// editor's space. <paramref name="growOnly"/> (the FX window) never makes the window smaller; otherwise (a popped-out editor)
    /// the window takes the editor's size exactly.
    public static Rect FitWindow(Rect window, (double Width, double Height) chrome, (double Width, double Height) editor, Rect work, bool growOnly)
    {
        var w = Math.Min(chrome.Width + editor.Width, work.Width - WorkAreaMargin);
        var h = Math.Min(chrome.Height + editor.Height, work.Height - WorkAreaMargin);
        if (growOnly)
        {
            w = Math.Max(window.Width, w);
            h = Math.Max(window.Height, h);
        }
        if (Math.Abs(w - window.Width) < BarSlack && Math.Abs(h - window.Height) < BarSlack) return window;
        var x = Math.Max(work.Left, Math.Min(window.Left, work.Right - w));
        var y = Math.Max(work.Top, Math.Min(window.Top, work.Bottom - h));
        return new Rect(x, y, w, h);
    }

    /// Which bars an editor needs in a space (a bar takes <paramref name="bar"/>, the app's scroll bar thickness, from the other way)
    /// and the scroll ranges (the overflow).
    public static ScrollLayout Scroll(double spaceWidth, double spaceHeight, double editorWidth, double editorHeight, double bar)
    {
        bool h = editorWidth > spaceWidth + BarSlack, v = editorHeight > spaceHeight + BarSlack;
        if (h && !v) v = editorHeight > spaceHeight - bar + BarSlack;
        if (v && !h) h = editorWidth > spaceWidth - bar + BarSlack;
        double viewW = Math.Max(0, spaceWidth - (v ? bar : 0)), viewH = Math.Max(0, spaceHeight - (h ? bar : 0));
        return new ScrollLayout(Math.Min(editorWidth, viewW), Math.Min(editorHeight, viewH), h ? editorWidth - viewW : 0, v ? editorHeight - viewH : 0);
    }

    /// Where the plug-in's view goes inside its host window for a scroll offset in DIPs (pixels, zero or negative).
    public static (int X, int Y) ViewOrigin(double offsetX, double offsetY, double scale) =>
        (-(int)Math.Round(offsetX * scale), -(int)Math.Round(offsetY * scale));
}

/// How a plug-in editor's space follows the editor's size.
public enum EditorSizing
{
    /// The FX window: the window grows to show the editor (never shrinks), within the screen.
    GrowWindow,

    /// A popped-out editor window: the window takes the editor's size, within the screen.
    FitWindow,
}
