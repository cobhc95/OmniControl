using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Volume.Fx;

/// Drawing a curve over a row of vertical sliders (a graphic equaliser): press on one slider and drag sideways; every slider the pointer
/// passes is set to the pointer's height, and a fast sweep fills the sliders it skipped along a straight line. The pressed slider
/// moves by itself as usual. Set <c>SliderPaint.IsEnabled="True"</c> on the control that holds the sliders. Input wiring only: the
/// sliders' bindings carry the values to their view-models.
public static class SliderPaint
{
    public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(SliderPaint));

    /// While a stroke runs: the column painted last (-1 = no stroke) and the pointer's height there, per host.
    private static readonly AttachedProperty<int> LastColumnProperty = AvaloniaProperty.RegisterAttached<Control, int>("LastColumn", typeof(SliderPaint), -1);
    private static readonly AttachedProperty<double> LastYProperty = AvaloniaProperty.RegisterAttached<Control, double>("LastY", typeof(SliderPaint));

    public static bool GetIsEnabled(Control host) => host.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control host, bool value) => host.SetValue(IsEnabledProperty, value);

    static SliderPaint() => IsEnabledProperty.Changed.AddClassHandler<Control>((host, e) =>
    {
        host.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        host.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        host.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        host.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        if (e.NewValue is not true) return;
        // Handled events too: the slider's own parts take the press and the moves of the stroke.
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, handledEventsToo: true);
    });

    private static List<Slider> Sliders(Control host) =>
        host.GetVisualDescendants().OfType<Slider>().Where(s => s.Orientation == Orientation.Vertical && s.IsEffectivelyEnabled).ToList();

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control host || !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
        if (e.Source is not Visual source || source.FindAncestorOfType<Slider>(true) is not { } pressed) return;
        var at = Sliders(host).IndexOf(pressed);
        if (at < 0) return;
        host.SetValue(LastColumnProperty, at);
        host.SetValue(LastYProperty, e.GetPosition(host).Y);
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control host) return;
        var last = host.GetValue(LastColumnProperty);
        if (last < 0) return;
        if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) { End(host); return; }
        var sliders = Sliders(host);
        var p = e.GetPosition(host);
        var at = sliders.FindIndex(s => ColumnOf(s, host) is { } c && p.X >= c.Left && p.X < c.Right);
        if (at < 0 || at == last) return;
        var lastY = host.GetValue(LastYProperty);
        var step = at > last ? 1 : -1;
        for (var i = last + step; i != at + step; i += step)
        {
            var share = (double)(i - last) / (at - last);
            SetFromHeight(sliders[i], host, lastY + share * (p.Y - lastY));
        }
        host.SetValue(LastColumnProperty, at);
        host.SetValue(LastYProperty, p.Y);
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control host) End(host);
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is Control host) End(host);
    }

    private static void End(Control host) => host.SetValue(LastColumnProperty, -1);

    /// The slider's column in the host's coordinates.
    private static Rect? ColumnOf(Slider slider, Control host) =>
        slider.TranslatePoint(default, host) is { } origin ? new Rect(origin, slider.Bounds.Size) : null;

    /// Sets a slider to the value its knob would have at height <paramref name="y"/> (host coordinates), as its own track does under a press.
    private static void SetFromHeight(Slider slider, Control host, double y)
    {
        if (slider.GetVisualDescendants().OfType<Track>().FirstOrDefault() is not { } track || host.TranslatePoint(new Point(0, y), track) is not { } p) return;
        var thumb = track.Thumb?.Bounds.Height ?? 0;
        var travel = Math.Max(1, track.Bounds.Height - thumb);
        var share = Math.Clamp((track.Bounds.Height - thumb / 2 - p.Y) / travel, 0, 1); // the minimum is at the bottom
        if (track.IsDirectionReversed) share = 1 - share;
        slider.Value = Math.Round(slider.Minimum + share * (slider.Maximum - slider.Minimum));
    }
}
