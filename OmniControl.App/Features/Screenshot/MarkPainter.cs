using Avalonia.Media.Imaging;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// Keeps the area overlay's bright picture (what the selection shows: the frozen picture with its drawings) in step with the drawings. It
/// compares the drawings with those it painted last and repaints only where one came, went or changed (<see cref="Mark.Covers"/>), each
/// part drawn by <see cref="ImageBitmaps.Render"/>, the same drawing the export makes. Any number of changes between two frames cost one
/// update, so it is called once per frame.
public sealed class MarkPainter
{
    /// Two parts are painted as one when the rectangle around both is at most this many times their own areas together.
    private const double MergeSlack = 1.5;

    private readonly Bitmap _picture;
    private readonly ScreenRect _bounds;
    private readonly Action<ScreenRect, ScreenImage> _paint;
    private readonly HashSet<Mark> _painted = new();
    private Mark? _preview;
    private int _previewVersion;
    private List<Rect> _previewChanged = new();

    /// <param name="picture">The frozen picture (not owned).</param>
    /// <param name="paint">Takes the repainted pixels of an area of the picture.</param>
    public MarkPainter(Bitmap picture, Action<ScreenRect, ScreenImage> paint)
    {
        _picture = picture;
        _bounds = new ScreenRect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height);
        _paint = paint;
    }

    /// Repaints what changed since the last call: finished drawings that came or went, and the drawing in progress.
    public void Update(IReadOnlyList<Mark> marks, Mark? preview)
    {
        var dirty = new List<Rect>();
        var current = marks.ToHashSet();
        foreach (var gone in _painted.Where(mark => !current.Contains(mark)).ToList())
        {
            dirty.AddRange(gone.Covers());
            _painted.Remove(gone);
        }
        foreach (var mark in marks)
            if (_painted.Add(mark)) dirty.AddRange(mark.Covers());

        if (preview != _preview)
        {
            // where the last drawing in progress was painted (a line may have moved once more as it was let go), and all of it when it
            // ended without becoming a drawing (the blur's frame, an empty blur), so it leaves no trace
            dirty.AddRange(_previewChanged);
            if (_preview is not null && !_painted.Contains(_preview)) dirty.AddRange(_preview.Covers());
            _preview = preview;
            _previewVersion = 0;
            _previewChanged = new List<Rect>();
        }
        var inProgress = preview is not null && !current.Contains(preview) ? preview : null;
        if (inProgress is not null)
        {
            var now = inProgress.CoversChangedSince(_previewVersion).ToList();
            dirty.AddRange(_previewChanged); // where it was last time (a line or box that moved)
            dirty.AddRange(now);
            _previewChanged = now;
            _previewVersion = inProgress.Version;
        }

        foreach (var area in Merge(dirty))
            _paint(area, ImageBitmaps.Render(_picture, area, marks, inProgress));
    }

    /// The areas in whole pixels inside the picture, overlapping ones joined so no pixel is painted many times.
    private List<ScreenRect> Merge(IEnumerable<Rect> dirty)
    {
        var areas = new List<ScreenRect>();
        foreach (var rect in dirty)
        {
            var left = (int)Math.Floor(rect.X);
            var top = (int)Math.Floor(rect.Y);
            var area = new ScreenRect(left, top, (int)Math.Ceiling(rect.Right) - left, (int)Math.Ceiling(rect.Bottom) - top).Intersect(_bounds);
            if (!area.IsEmpty) Add(areas, area);
        }
        return areas;
    }

    private static void Add(List<ScreenRect> areas, ScreenRect area)
    {
        for (var i = 0; i < areas.Count; i++)
        {
            var both = areas[i].Union(area);
            if (Size(both) > MergeSlack * (Size(areas[i]) + Size(area))) continue;
            areas.RemoveAt(i);
            Add(areas, both); // the joined area may now join another
            return;
        }
        areas.Add(area);
    }

    private static long Size(ScreenRect r) => (long)r.Width * r.Height;
}
