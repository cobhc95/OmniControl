namespace OmniControl.App.Features.Icons;

/// What an icon is drawn from (<see cref="Shared.Ui.Controls.IconGlyph"/>): a shape on a 24 x 24 grid drawn as strokes (<paramref name="Stroked"/>,
/// <paramref name="StrokeWidth"/> in grid units: the library's and an outline SVG of the user's), a filled shape that fills the glyph (a
/// page's icon, a filled SVG), or a picture (a PNG of the user's).
public sealed record IconImage(Geometry? Shape, bool Stroked = false, double StrokeWidth = IconImage.LibraryStroke, IImage? Picture = null)
{
    /// The library icons' stroke on their 24 x 24 grid.
    public const double LibraryStroke = 2;

    public static IconImage Outline(Geometry shape, double strokeWidth = LibraryStroke) => new(shape, true, strokeWidth);

    public static IconImage Filled(Geometry shape) => new(shape);

    public static IconImage Of(IImage picture) => new(null, Picture: picture);
}

/// Turns an icon value into what is drawn. A value is a library id (<see cref="IconLibrary"/>, its drawing in Styles/Icons.axaml), a user
/// icon (<see cref="IconStore"/> puts each one in the application's resources under its value), or a value saved before the library
/// (read as its replacement); when it names nothing, the page's icon (<paramref name="pagePath"/>, path data) stands in.
public static class IconResources
{
    /// The icon with this value; null when there is none (no value, an unknown one, or no running application).
    public static IconImage? Find(string? value)
    {
        if (value is null) return null;
        if (IconLibrary.Find(value) is { } icon) return Resource(icon.Resource) is Geometry shape ? IconImage.Outline(shape) : null;
        return Resource(value) as IconImage;
    }

    /// The entry's own icon, or else the section's one (<paramref name="pagePath"/>: path data, or a library id); null when neither exists.
    public static IconImage? Of(string? value, string? pagePath) =>
        Find(value) ?? (string.IsNullOrEmpty(pagePath) ? null : IconLibrary.Find(pagePath) is not null ? Find(pagePath) : IconImage.Filled(Geometry.Parse(pagePath)));

    private static object? Resource(string key) => Application.Current?.TryFindResource(key, out var found) == true ? found : null;
}
