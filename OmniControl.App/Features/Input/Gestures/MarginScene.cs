using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// One name label to draw, at <see cref="Rect"/> in screen pixels.
internal readonly record struct MarginLabelItem(ScreenRect Rect, string Text, bool Outlined);

/// Everything one monitor's margin window shows: the monitor's rectangle and display scaling (screen pixels and DIPs per pixel), the
/// area rectangles and the labels, all in screen pixels. Compared with <see cref="SameAs"/> so the window redraws only on a change.
internal sealed record MarginScene(ScreenRect Screen, double Scale, Color Accent, IReadOnlyList<ScreenArea> Areas, IReadOnlyList<MarginLabelItem> Labels)
{
    public bool SameAs(MarginScene other) =>
        Screen == other.Screen && Scale == other.Scale && Accent == other.Accent
        && Areas.SequenceEqual(other.Areas) && Labels.SequenceEqual(other.Labels);

    /// The parts of the monitor that are drawn on; everything else of the window is cut away.
    public List<ScreenRect> Visible() =>
        Areas.Select(a => a.Rect).Concat(Labels.Select(l => l.Rect)).Select(r => r.Intersect(Screen)).Where(r => !r.IsEmpty).ToList();
}
