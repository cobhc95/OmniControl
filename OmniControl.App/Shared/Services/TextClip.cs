using System.Globalization;

namespace OmniControl.App.Shared.Services;

/// Text that came from outside (another PC, a shared device set-up) made safe to show: no control or format characters (a right-to-left
/// override would make it read as something else), trimmed and cut to a length.
public static class TextClip
{
    public static string Clean(string? text, int max)
    {
        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray()).Trim();
        return clean.Length > max ? clean[..max] : clean;
    }
}
