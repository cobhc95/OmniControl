using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OmniControl.App.Gestures;
using OmniControl.App.Osd;
using OmniControl.App.Pages;

namespace OmniControl.App.Showcase;

/// <summary>
/// Takes the pictures: each page of the main window (and each of its tabs) at the window's default and minimum size, then each window the
/// shell registered, in both themes, into <c>&lt;folder&gt;/&lt;theme&gt;/&lt;NN&gt;-&lt;name&gt;[-&lt;tab&gt;][-min].png</c>. Rendering is offscreen
/// (<see cref="RenderTargetBitmap"/>), personal names are covered by <see cref="ShowcaseScrub"/> while it renders, and the window,
/// page, tab, view and theme the owner had are put back at the end. A page with an Easy and an Advanced view (<c>IsEasy</c>) is taken in
/// both, and each of its tabs in Advanced. Nothing here is saved to the settings. <c>privacy-report.txt</c> in the folder says, per
/// picture, what was replaced, blurred or hidden.
/// </summary>
internal sealed partial class ShowcaseExporter
{
    /// <summary>A dev-only pause for a page or window to lay out and fill (devices, lists); too short shows a half-built picture.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(700);

    private const double Scale = 2; // pixels per device-independent pixel in the files: sharp on a GitHub page

    private readonly Window _window;
    private readonly double _defaultWidth;
    private readonly double _defaultHeight;
    private readonly IShowcaseSource _source;
    private readonly string _folder;
    private readonly bool _isolated;
    private readonly bool _keepNames;
    private readonly List<(string File, IReadOnlyList<string> Done)> _privacy = new();
    private int _number;

    /// <param name="isolated">The run uses throw-away data, so windows that make data (<see cref="ShowcaseWindow.Isolated"/>) may open.</param>
    /// <param name="keepNames">Device, monitor and app names stay as they are (a run on the owner's real setup); the personal names are still covered.</param>
    public ShowcaseExporter(Window main, IShowcaseSource source, string folder, bool isolated, bool keepNames = false)
    {
        (_window, _source, _folder, _isolated, _keepNames) = (main, source, folder, isolated, keepNames);
        (_defaultWidth, _defaultHeight) = (main.Width, main.Height); // the size the window has when the run begins is its default
    }

    /// <summary>Everything: both themes, pages, then windows. Returns how many files were written.</summary>
    public async Task<int> RunAsync()
    {
        var app = Application.Current ?? throw new InvalidOperationException("No application.");
        var theme = app.RequestedThemeVariant;
        var window = _window;
        var (width, height, state) = (window.Width, window.Height, window.WindowState);
        var openPage = _source.ShowcaseCurrentPage;
        var written = 0;
        try
        {
            window.WindowState = WindowState.Normal;
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                app.RequestedThemeVariant = variant;
                _number = 0;
                var folder = Path.Combine(_folder, variant.Key.ToString()!.ToLowerInvariant());
                Directory.CreateDirectory(folder);
                written += await PagesAsync(folder);
                written += await WindowsAsync(folder);
            }
        }
        finally
        {
            app.RequestedThemeVariant = theme;
            (window.Width, window.Height, window.WindowState) = (width, height, state);
            if (openPage is not null) _source.ShowcaseShow(openPage);
        }
        WritePrivacyReport();
        return written;
    }

    private async Task<int> PagesAsync(string folder)
    {
        var written = 0;
        var window = _window;
        foreach (var page in _source.ShowcasePages)
        {
            _source.ShowcaseShow(page);
            var tabs = TabsOf(page);
            var firstTab = tabs?.Current;
            var easy = EasyViewOf(page);
            var firstView = easy?.GetValue(page);
            var number = ++_number;
            foreach (var (view, shows) in ViewsOf(easy))
            {
                if (shows is not null) easy!.SetValue(page, shows);
                // Tabs belong to the Advanced view only: Easy has none, so it is taken once.
                var tabNames = shows is true ? new string?[] { null } : tabs?.Items.Select(t => (string?)t.Name) ?? new string?[] { null };
                foreach (var tab in tabNames)
                {
                    if (tab is not null) tabs!.Select(tab);
                    foreach (var minimum in new[] { false, true })
                    {
                        (window.Width, window.Height) = minimum ? (window.MinWidth, window.MinHeight) : (_defaultWidth, _defaultHeight);
                        await Task.Delay(Settle);
                        await OpenGroupsAsync(window);
                        written += Save(window, Path.Combine(folder, FileName(number, page.Title, new[] { view, tab }, minimum)));
                    }
                }
            }
            if (firstTab is not null) tabs!.Select(firstTab);
            if (firstView is not null) easy!.SetValue(page, firstView);
        }
        return written;
    }

    /// <summary>The page's views as (name for the file, value of its Easy switch): one unnamed view for a page that has no such switch.</summary>
    private static IEnumerable<(string? Name, bool? Easy)> ViewsOf(PropertyInfo? easy) =>
        easy is null ? new (string?, bool?)[] { (null, null) } : new (string?, bool?)[] { ("easy", true), ("advanced", false) };

    /// <summary>The page's own Easy / Advanced switch (a settable <c>IsEasy</c> flag), or null when it has one view.</summary>
    private static PropertyInfo? EasyViewOf(PageViewModel page) =>
        page.GetType().GetProperty("IsEasy", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } p && p.PropertyType == typeof(bool) ? p : null;

    private async Task<int> WindowsAsync(string folder)
    {
        var written = 0;
        foreach (var entry in _source.ShowcaseWindows.Where(w => _isolated || !w.Isolated))
        {
            var before = OpenWindows();
            entry.Open();
            await Task.Delay(Settle);
            if (OpenWindows().Except(before).FirstOrDefault(w => w is not (OsdWindow or MarginWindow)) is not { } shown) continue;
            var number = ++_number;
            shown.ShowInTaskbar = false;
            shown.Position = new PixelPoint(-32000, -32000);
            var (width, height) = (shown.Width, shown.Height);
            foreach (var minimum in new[] { false, true })
            {
                if (minimum && (shown.MinWidth <= 0 || shown.MinHeight <= 0)) break;
                if (minimum) (shown.Width, shown.Height) = (shown.MinWidth, shown.MinHeight);
                await Task.Delay(Settle);
                await OpenGroupsAsync(shown);
                written += Save(shown, Path.Combine(folder, FileName(number, entry.Name, Array.Empty<string?>(), minimum)));
            }
            (shown.Width, shown.Height) = (width, height);
            if (entry.Finish is { } finish) finish(shown);
            else shown.Close();
            await Task.Delay(Settle);
        }
        return written;
    }

    /// <summary>
    /// Opens every group in the window (each shows its header through <see cref="GroupHeader"/>), so a picture shows what is inside it (a group opened here may hold more groups, hence the
    /// repeat). The open state is not saved: the run's data is a throw-away copy, and it is still not written back.
    /// </summary>
    private static async Task OpenGroupsAsync(Window window)
    {
        for (var pass = 0; pass < MaxGroupPasses; pass++)
        {
            var closed = window.GetVisualDescendants().OfType<GroupHeader>().Select(h => h.DataContext).OfType<CardGroup>().Where(g => !g.IsExpanded).Distinct().ToList();
            if (closed.Count == 0) return;
            foreach (var group in closed) group.OpenWithoutSaving();
            await Task.Delay(Settle);
        }
    }

    private const int MaxGroupPasses = 4;

    private static IReadOnlyList<Window> OpenWindows() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.ToList() ?? new List<Window>();

    /// <summary>The page's tabs, found on the page's own <see cref="PageTabs"/> property (null: the page has none).</summary>
    private static PageTabs? TabsOf(PageViewModel page) => page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(PageTabs)).Select(p => p.GetValue(page) as PageTabs).FirstOrDefault();

    private int Save(Window window, string path)
    {
        window.UpdateLayout();
        using var scrub = new ShowcaseScrub(_keepNames ? Array.Empty<string>() : _source.ShowcasePrivateNames());
        scrub.Apply(window);
        window.UpdateLayout();
        var size = window.Bounds.Size;
        var pixels = new PixelSize((int)(size.Width * Scale), (int)(size.Height * Scale));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * Scale, 96 * Scale));
        bitmap.Render(window);
        bitmap.Save(path);
        _privacy.Add((Path.GetRelativePath(_folder, path), scrub.Report));
        return 1;
    }

    /// <summary>One line per picture: what was replaced, blurred or hidden in it (kinds and counts, never the words themselves).</summary>
    private void WritePrivacyReport()
    {
        var lines = new List<string>
        {
            "Privacy report of the screenshot export (kinds and counts only; no personal text is written here).",
            _keepNames ? "Device, monitor and app names were kept as they are." : "Device and monitor names were replaced.",
            "Replaced: the user, machine and profile names, emails, IP and MAC addresses. Blurred: such text a replacement could not reach. Hidden behind dots: text boxes holding it.",
            string.Empty,
        };
        lines.AddRange(_privacy.Select(p => $"{p.File}: {(p.Done.Count == 0 ? "nothing needed covering" : string.Join("; ", p.Done))}"));
        File.WriteAllLines(Path.Combine(_folder, "privacy-report.txt"), lines);
        _privacy.Clear();
    }

    private static string FileName(int number, string name, IEnumerable<string?> parts, bool minimum) =>
        $"{number:00}-{Slug(name)}{string.Concat(parts.Where(p => p is not null).Select(p => "-" + Slug(p!)))}{(minimum ? "-min" : "")}.png";

    private static string Slug(string text) => Unsafe().Replace(text.Trim().ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex Unsafe();
}
