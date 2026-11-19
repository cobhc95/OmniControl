using System.Globalization;

namespace OmniControl.App.Features.Screenshot;

/// The file name rule of a saved screenshot: the pattern's {date}, {time} and {app} filled in, characters Windows does not allow in a file
/// name taken out, and " (2)", " (3)"... added when a file of that name is already there.
public static class FileNamePattern
{
    public const string DateToken = "{date}", TimeToken = "{time}", AppToken = "{app}";
    private const string DateFormat = "yyyy-MM-dd", TimeFormat = "HH-mm-ss", Fallback = "Screenshot";

    /// The file name without its ending.
    public static string Make(string pattern, DateTime at, string app)
    {
        var name = pattern
            .Replace(DateToken, at.ToString(DateFormat, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace(TimeToken, at.ToString(TimeFormat, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace(AppToken, app, StringComparison.OrdinalIgnoreCase);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' '); // an empty {app} leaves no double space
        return name.Length > 0 ? name : Fallback;
    }

    /// The full path for the name in the folder, with " (2)", " (3)"... when <paramref name="exists"/> says the path is taken.
    public static string UniquePath(string folder, string name, string extension, Func<string, bool> exists)
    {
        var path = Path.Combine(folder, name + extension);
        for (var n = 2; exists(path); n++) path = Path.Combine(folder, $"{name} ({n}){extension}");
        return path;
    }
}
