using Avalonia;
using Avalonia.Controls;
using OmniControl.App.Screenshot;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Showcase;

/// <summary>
/// The screenshot area editor for the export: the real overlay window over a made-up picture (never a capture of the screen), with a
/// sample selection and a few drawings made through the view-model's own pointer calls. Nothing is saved, copied or printed
/// (<see cref="NoOutput"/>), and no screen is read.
/// </summary>
internal sealed class ShowcaseCapture
{
    private const int Width = 1280, Height = 720;
    private const double Reach = 8;
    private CaptureOverlayViewModel? _model;

    /// <summary>Opens the overlay with the sample selection and drawings (it is shown off screen by the exporter).</summary>
    public void Open()
    {
        var monitor = new ScreenRect(0, 0, Width, Height);
        var sizes = new ToolSizes(new Dictionary<string, double>(), () => { });
        var model = _model = new CaptureOverlayViewModel(Picture(), monitor, new NoOutput(), () => { }, recordAtOnce: false, sizes) { Scale = 1 };
        var window = new CaptureOverlay(model, monitor, strong: false);
        Drag(model, DrawTool.None, new Point(300, 150), new Point(980, 560)); // the selection
        Drag(model, DrawTool.Box, new Point(380, 230), new Point(640, 340));
        Drag(model, DrawTool.Arrow, new Point(820, 250), new Point(690, 320));
        model.AddText(new Point(660, 440), "Sample");
        Drag(model, DrawTool.Blur, new Point(380, 420), new Point(640, 500));
        model.Tool = DrawTool.Box;
        // The overlay makes itself full screen when it opens; this shows it at its own size instead, off the desktop.
        window.Opened += (_, _) => (window.WindowState, window.Position, window.Width, window.Height) = (WindowState.Normal, new PixelPoint(-32000, -32000), Width, Height);
        window.ShowInTaskbar = false;
        window.Show();
    }

    /// <summary>Closes the overlay and frees its picture.</summary>
    public void Finish(Window window)
    {
        window.Close();
        _model?.Dispose();
        _model = null;
    }

    private static void Drag(CaptureOverlayViewModel model, DrawTool tool, Point from, Point to)
    {
        model.Tool = tool;
        model.Press(from, Reach);
        model.MoveTo(to);
        model.Release(to);
    }

    /// <summary>A soft gradient with a few blocks, standing for a desktop.</summary>
    private static ScreenImage Picture()
    {
        var pixels = new byte[Width * Height * ScreenImage.BytesPerPixel];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var at = (y * Width + x) * ScreenImage.BytesPerPixel;
                var block = Block(x, y);
                pixels[at] = (byte)(block ?? 150 + 80 * x / Width);      // blue
                pixels[at + 1] = (byte)(block ?? 110 + 60 * y / Height); // green
                pixels[at + 2] = (byte)(block ?? 90 + 40 * (x + y) / (Width + Height)); // red
                pixels[at + 3] = 255;
            }
        return new ScreenImage(Width, Height, pixels);
    }

    /// <summary>A grey level inside one of the made-up windows, or null on the backdrop.</summary>
    private static int? Block(int x, int y)
    {
        if (x is > 340 and < 940 && y is > 180 and < 520) return y < 215 ? 70 : 235; // a window with its title bar
        return null;
    }

    private sealed class NoOutput : IShotOutput
    {
        public void Copy(ScreenImage image) { }
        public void Save(ScreenImage image) { }
        public void Print(ScreenImage image) { }
        public void Record(ScreenRect area, ScreenRect monitor, double scale) { }
        public void SearchOnline(ScreenImage image) { }
        public Task<IReadOnlyList<string>> ReadQrAsync(ScreenImage image) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public void CopyText(string text) { }
        public void OpenLink(string url) { }
    }
}
