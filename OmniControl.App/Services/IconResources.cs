using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OmniControl.App.Icons;

namespace OmniControl.App.Services;

/// <summary>
/// What an icon is drawn from (<see cref="Controls.IconGlyph"/>): a shape on a 24 x 24 grid drawn as strokes (<paramref name="Stroked"/>,
/// <paramref name="StrokeWidth"/> in grid units: the library's and an outline SVG of the user's), a filled shape that fills the glyph (a
/// page's icon, a filled SVG), or a picture (a PNG of the user's).
/// </summary>
public sealed record IconImage(Geometry? Shape, bool Stroked = false, double StrokeWidth = IconImage.LibraryStroke, IImage? Picture = null)
{
    /// <summary>The library icons' stroke on their 24 x 24 grid.</summary>
    public const double LibraryStroke = 2;

    public static IconImage Outline(Geometry shape, double strokeWidth = LibraryStroke) => new(shape, true, strokeWidth);

    public static IconImage Filled(Geometry shape) => new(shape);

    public static IconImage Of(IImage picture) => new(null, Picture: picture);
}

/// <summary>
/// Turns an icon value into what is drawn. A value is a library id (<see cref="IconLibrary"/>, its drawing in Styles/Icons.axaml), a user
/// icon (<see cref="IconStore"/> puts each one in the application's resources under its value), or a value saved before the library
/// (read as its replacement); when it names nothing, the page's icon (<paramref name="pagePath"/>, path data) stands in.
/// </summary>
public static class IconResources
{
    /// <summary>The icon with this value; null when there is none (no value, an unknown one, or no running application).</summary>
    public static IconImage? Find(string? value)
    {
        if (value is null) return null;
        if (IconLibrary.Find(value) is { } icon) return Resource(icon.Resource) is Geometry shape ? IconImage.Outline(shape) : null;
        return Resource(value) as IconImage;
    }

    /// <summary>The entry's own icon, or else the page's one (<paramref name="pagePath"/>, path data); null when neither exists.</summary>
    public static IconImage? Of(string? value, string? pagePath) =>
        Find(value) ?? (string.IsNullOrEmpty(pagePath) ? null : IconImage.Filled(Geometry.Parse(pagePath)));

    private static object? Resource(string key) => Application.Current?.TryFindResource(key, out var found) == true ? found : null;
}
