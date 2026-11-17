using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Gestures;

/// <summary>
/// The on-screen gesture margins of one monitor: a see-through window covering the whole monitor, cut by a window region down to the
/// area rectangles and labels (<see cref="OverlayPlacement"/>), so a 2 px strip is 2 px (a small window of its own would be made
/// bigger by Windows' minimum window size) and the rest of the monitor is untouched. Topmost, never takes focus, ignores the mouse,
/// set up like the OSD pop-up (<c>OsdWindow</c>). Its content is rebuilt only when the scene changes.
/// </summary>
internal sealed class MarginWindow : Window
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };

    // What is shown now, and whether Windows accepted its placement (a refused one is retried at the next refresh).
    private MarginScene? _scene;
    private bool _placed;

    public MarginWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        MinWidth = 0;
        MinHeight = 0;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Content = _canvas;
    }

    public void ShowScene(MarginScene scene)
    {
        if (IsVisible && _placed && _scene is { } shown && shown.SameAs(scene)) return;
        if (_scene is null || !_scene.SameAs(scene)) Rebuild(scene);
        _scene = scene;
        if (!IsVisible)
        {
            // Roughly in place before it first appears; the exact pixels follow at once.
            Position = new PixelPoint(scene.Screen.Left, scene.Screen.Top);
            Width = scene.Screen.Width / scene.Scale;
            Height = scene.Screen.Height / scene.Scale;
            Show();
        }
        _placed = TryGetPlatformHandle() is { } handle && OverlayPlacement.Place(handle.Handle, scene.Screen, scene.Visible());
        ClickThrough.Apply(this); // every time: Windows can drop the click-through style when the window changes
    }

    /// <summary>Takes the window off the screen; it is shown again by the next <see cref="ShowScene"/>.</summary>
    public void HideScene()
    {
        _scene = null;
        _placed = false;
        if (IsVisible) Hide();
    }

    private void Rebuild(MarginScene scene)
    {
        _canvas.Children.Clear();
        foreach (var area in scene.Areas)
            Add(new MarginArea(area.Kind, scene.Accent) { Width = area.Rect.Width / scene.Scale, Height = area.Rect.Height / scene.Scale }, area.Rect, scene);
        foreach (var label in scene.Labels)
            Add(MarginLabel.Build(label.Text, scene.Accent, label.Outlined), label.Rect, scene);
    }

    private void Add(Control control, ScreenRect rect, MarginScene scene)
    {
        Canvas.SetLeft(control, (rect.Left - scene.Screen.Left) / scene.Scale);
        Canvas.SetTop(control, (rect.Top - scene.Screen.Top) / scene.Scale);
        _canvas.Children.Add(control);
    }
}
