using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Platform;
using OmniControl.App.Shared.Ui.Restore;
using SkiaSharp;

namespace OmniControl.Screenshots;

/// Puts a view in an off-screen window, lets layout and the dispatcher settle, takes the frame Skia drew and saves it as a small PNG
/// (cropped to its content, most compact PNG filters and zlib level 9).
internal sealed class Camera(string folder)
{
    /// The main window's page area at its usual size (a window about 1040 wide with the sidebar).
    public const double PageWidth = 860, PageHeight = 720;

    public List<(string File, long Bytes)> Saved { get; } = new();

    /// A sidebar page as the main window shows it: its title line (unless it draws its own), then its view, in the page area's scroller.
    public void Page(PageViewModel page, string name, double height = PageHeight, Action<Control>? prepare = null)
    {
        var body = new StackPanel { Margin = new Thickness(12, 12, 12, 24) };
        if (page.Restorer is null && page.Restorable is { } part)
            page.AttachRestorer(new PageRestore(new RestoreRunner(part.Key, page.Title, new[] { part }, false, _ => Task.FromResult(false), new FileSnapshotStore())));
        page.Restorer?.Refresh();
        if (!page.ShowsOwnHeader)
        {
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, Margin = new Thickness(0, 0, 0, 8) };
            title.Children.Add(new TextBlock { Text = page.Title, Classes = { "title" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var restore = new RestoreBar { DataContext = page.Restorer };
            Grid.SetColumn(restore, 1);
            title.Children.Add(restore);
            body.Children.Add(title);
        }
        var view = Build(page);
        body.Children.Add(view);
        Shoot(new Window { Width = PageWidth, Height = height, Content = new ScrollViewer { Content = body } }, name, crop: true, () => prepare?.Invoke(view));
    }

    /// A view of fixed size.
    public void Sized(Control content, string name, double width, double height, bool crop = false, Action? prepare = null) =>
        Shoot(new Window { Width = width, Height = height, Content = content }, name, crop, prepare);

    /// A view sized to its own content.
    public void Fit(Control content, string name, Action? prepare = null) =>
        Shoot(new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = content }, name, crop: false, prepare);

    /// A window the app makes itself, at its own size (or the one given).
    public void Window(Window window, string name, bool crop = false, Action? prepare = null) => Shoot(window, name, crop, prepare);

    /// The view the app's templates make for <paramref name="model"/>, with it as its data.
    public static Control Build(object model)
    {
        var view = (Application.Current!.DataTemplates.FirstOrDefault(t => t.Match(model)) ?? FuncDataTemplate.Default).Build(model)
                   ?? throw new InvalidOperationException($"No view for {model.GetType().Name}.");
        view.DataContext = model;
        return view;
    }

    /// Lets queued work, layout and rendering run a few rounds (views that build parts when first shown post them).
    public static void Settle(int rounds = 6)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// Lets work started on worker threads (a page reading its sample devices) post its results, for <paramref name="milliseconds"/>.
    public static void Pump(int milliseconds = 600)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            Settle(1);
            Thread.Sleep(15);
        }
    }

    private void Shoot(Window window, string name, bool crop, Action? prepare)
    {
        window.Show();
        Pump(300);
        prepare?.Invoke();
        Pump(300);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was drawn.");
        window.Close();
        Settle(2);
        Save(frame, name, crop);
    }

    private void Save(Avalonia.Media.Imaging.WriteableBitmap frame, string name, bool crop)
    {
        using var buffer = frame.Lock();
        var type = buffer.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var info = new SKImageInfo(buffer.Size.Width, buffer.Size.Height, type, SKAlphaType.Premul);
        using var whole = new SKBitmap();
        whole.InstallPixels(info, buffer.Address, buffer.RowBytes);
        var height = crop ? ContentBottom(whole) : whole.Height;
        using var part = new SKBitmap();
        whole.ExtractSubset(part, new SKRectI(0, 0, whole.Width, height));
        using var pixels = part.PeekPixels();
        using var png = pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9));
        var path = Path.Combine(folder, name + ".png");
        using (var file = File.Create(path)) png.SaveTo(file);
        Saved.Add((name + ".png", new FileInfo(path).Length));
    }

    /// The last row that differs from the bottom-right background, plus a margin like the page's own.
    private static int ContentBottom(SKBitmap bitmap)
    {
        var background = bitmap.GetPixel(bitmap.Width - 2, bitmap.Height - 2);
        for (var y = bitmap.Height - 1; y >= 0; y--)
        for (var x = 0; x < bitmap.Width - 16; x++) // the scroll bar's strip at the right is left out
            if (bitmap.GetPixel(x, y) != background)
                return Math.Min(bitmap.Height, y + 16);
        return bitmap.Height;
    }
}
