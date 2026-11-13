namespace OmniControl.Core.Engine.Mouse;

/// <summary>
/// Maps where a window's title bar is by asking the window point by point with the gesture hit test's own question
/// (<see cref="PointerArea.IsCaption"/>), so the drawn title bar is the area the gesture really fires in.
/// Every row of the window's top is sampled every <see cref="SampleStep"/> pixels and the ends of each title-bar run are then found
/// to the exact pixel; a gap in the title bar narrower than <see cref="SampleStep"/> that falls between two samples is not seen.
/// Slow (thousands of questions for a wide window): call it off the UI thread and only when the window moved or changed.
/// </summary>
internal static class CaptionArea
{
    /// <summary>Pixels between samples along a row (caption buttons and window icons are wider than this).</summary>
    private const int SampleStep = 16;

    /// <summary>Rows from the window's top searched for the first title-bar row (the resize border sits above it).</summary>
    private const int LeadRows = 24;

    /// <summary>The deepest title bar looked for, in rows from the window's top.</summary>
    private const int MaxRows = 240;

    /// <summary>The title-bar rectangles of <paramref name="window"/> (outer bounds <paramref name="frame"/>); empty when it has none or stops answering.</summary>
    public static List<ScreenRect> Of(IntPtr window, ScreenRect frame)
    {
        var rects = new List<ScreenRect>();
        List<(int From, int To)>? band = null;
        int bandTop = 0, lastRow = 0;
        var found = false;

        for (var y = frame.Top; y < Math.Min(frame.Bottom, frame.Top + MaxRows); y++)
        {
            var runs = RunsAt(window, frame, y);
            if (runs is null) return new List<ScreenRect>(); // stopped answering: draw nothing rather than a guess
            if (runs.Count == 0)
            {
                if (found || y - frame.Top >= LeadRows) break;
                continue;
            }
            found = true;
            if (band is not null && !runs.SequenceEqual(band)) { Close(band, bandTop, y); band = null; }
            if (band is null) { band = runs; bandTop = y; }
            lastRow = y;
        }
        if (band is not null) Close(band, bandTop, lastRow + 1);
        return rects;

        void Close(List<(int From, int To)> runs, int top, int end)
        {
            foreach (var (from, to) in runs) rects.Add(new ScreenRect(from, top, to, end));
        }
    }

    /// <summary>The title-bar runs [From, To) on one row, or null when the window did not answer.</summary>
    private static List<(int From, int To)>? RunsAt(IntPtr window, ScreenRect frame, int y)
    {
        var runs = new List<(int From, int To)>();
        int? start = null;
        var previous = frame.Left - 1; // an imagined "not title bar" just left of the window, never asked
        var previousHit = false;
        for (var x = frame.Left; x < frame.Right; x = x == frame.Right - 1 ? frame.Right : Math.Min(x + SampleStep, frame.Right - 1))
        {
            if (PointerArea.IsCaption(window, x, y) is not { } hit) return null;
            if (hit != previousHit)
            {
                if (Edge(window, previous, x, y, hit) is not { } edge) return null;
                if (hit) start = edge;
                else if (start is { } from) { runs.Add((from, edge)); start = null; }
            }
            previous = x;
            previousHit = hit;
        }
        if (start is { } open) runs.Add((open, frame.Right));
        return runs;
    }

    /// <summary>
    /// Between <paramref name="from"/> (answer: not <paramref name="wantHit"/>) and <paramref name="to"/> (answer: <paramref name="wantHit"/>),
    /// the first pixel whose answer is <paramref name="wantHit"/>; null when the window stopped answering.
    /// </summary>
    private static int? Edge(IntPtr window, int from, int to, int y, bool wantHit)
    {
        while (to - from > 1)
        {
            var middle = from + (to - from) / 2;
            if (PointerArea.IsCaption(window, middle, y) is not { } hit) return null;
            if (hit == wantHit) to = middle; else from = middle;
        }
        return to;
    }
}
