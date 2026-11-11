using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OmniControl.App.Osd;

/// <summary>The pop-up: topmost, never takes focus, ignores the mouse. Its look comes from <see cref="OsdView"/>.</summary>
public sealed class OsdWindow : Window
{
    public OsdWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }

    public void Render(OsdSettings settings, string label, int value)
    {
        Content = OsdView.Build(settings, label, value);
        Opacity = Math.Clamp(settings.Opacity, 10, 100) / 100.0;
    }

    /// <summary>Shows the window at <paramref name="position"/> on the primary screen without stealing focus.</summary>
    public void ShowAt(OsdPosition position)
    {
        var screen = Screens.Primary;
        if (screen is null || Content is not Control content) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;

        // Measure first so the window can be placed before it is shown (otherwise it flashes in the centre).
        content.Measure(Size.Infinity);
        var w = (int)(content.DesiredSize.Width * scale) + 2;
        var h = (int)(content.DesiredSize.Height * scale) + 2;
        var margin = (int)(32 * scale);

        var x = position switch
        {
            OsdPosition.TopLeft or OsdPosition.BottomLeft => area.X + margin,
            OsdPosition.TopRight or OsdPosition.BottomRight => area.Right - w - margin,
            _ => area.X + (area.Width - w) / 2,
        };
        var y = position switch
        {
            OsdPosition.TopLeft or OsdPosition.TopCenter or OsdPosition.TopRight => area.Y + margin,
            OsdPosition.Center => area.Y + (area.Height - h) / 2,
            _ => area.Bottom - h - margin,
        };
        Position = new PixelPoint(x, y);
        if (!IsVisible) Show();
        MakeClickThrough();
        Position = new PixelPoint(x, y); // some platforms reset it on first show
    }

    private void MakeClickThrough()
    {
        if (TryGetPlatformHandle() is not { } handle) return;
        const int GWL_EXSTYLE = -20, TRANSPARENT = 0x20, TOOLWINDOW = 0x80, NOACTIVATE = 0x08000000;
        var style = GetWindowLongPtr(handle.Handle, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(handle.Handle, GWL_EXSTYLE, new IntPtr(style | TRANSPARENT | TOOLWINDOW | NOACTIVATE));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}
