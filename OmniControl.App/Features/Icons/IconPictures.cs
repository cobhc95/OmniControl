using Avalonia.Media.Imaging;

namespace OmniControl.App.Features.Icons;

/// Decodes an icon's picture at the size it is drawn instead of the file's, so a large photo is never held at full size.
internal static class IconPictures
{
    private const double Sharp = 2; // pixels per DIP: sharp up to 200 % display scaling (no window's scaling is known where pictures are made)

    /// <param name="stream">A seekable stream of the picture; the caller disposes it.</param>
    /// <param name="sizeKey">The App.axaml resource holding the box's size in DIPs.</param>
    /// <param name="fill">The box is filled (the short side matches); otherwise the picture fits inside it (the long side matches).</param>
    public static Bitmap Decode(Stream stream, string sizeKey, bool fill)
    {
        if (Application.Current?.TryFindResource(sizeKey, out var found) != true || found is not double dip) return new Bitmap(stream);
        var side = (int)Math.Ceiling(dip * Sharp);
        var byWidth = Bitmap.DecodeToWidth(stream, side);
        if (fill ? byWidth.PixelSize.Height >= side : byWidth.PixelSize.Height <= side) return byWidth;
        byWidth.Dispose();
        stream.Position = 0;
        return Bitmap.DecodeToHeight(stream, side);
    }
}
