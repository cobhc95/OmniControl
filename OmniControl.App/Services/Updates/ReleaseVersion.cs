namespace OmniControl.App.Services.Updates;

/// <summary>
/// A release version ("0.1.6", "v0.2.0-beta.1"): major, minor, patch and an optional pre-release tag. Ordered the semantic-versioning way:
/// numbers first, a version with a pre-release tag is older than the same version without one, tags compare part by part (numbers as numbers).
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string Pre) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        var s = (text ?? string.Empty).Trim().TrimStart('v', 'V');
        var plus = s.IndexOf('+'); // build metadata never counts
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        var pre = dash >= 0 ? s[(dash + 1)..] : string.Empty;
        var numbers = (dash >= 0 ? s[..dash] : s).Split('.');
        if (numbers.Length > 3 || numbers.Any(n => !int.TryParse(n, out var v) || v < 0)) return false;
        var part = numbers.Select(int.Parse).Concat(Enumerable.Repeat(0, 3)).Take(3).ToArray();
        version = new ReleaseVersion(part[0], part[1], part[2], pre);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var byNumbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumbers != 0) return byNumbers;
        if (Pre.Length == 0 && other.Pre.Length == 0) return 0;
        if (Pre.Length == 0) return 1;
        if (other.Pre.Length == 0) return -1;
        return ComparePre(Pre, other.Pre);
    }

    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    private static int ComparePre(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = int.TryParse(left[i], out var x);
            var rightIsNumber = int.TryParse(right[i], out var y);
            var c = leftIsNumber && rightIsNumber ? x.CompareTo(y)
                : leftIsNumber ? -1 // a number is older than words
                : rightIsNumber ? 1
                : string.CompareOrdinal(left[i], right[i]);
            if (c != 0) return Math.Sign(c);
        }
        return left.Length.CompareTo(right.Length);
    }
}
