namespace OmniControl.App.Services;

/// <summary>How a picked screen point is kept as text ("x,y", physical pixels) and shown ("x, y"); the one place that knows the format.</summary>
public static class ScreenPointText
{
    public static string Write(int x, int y) => $"{x},{y}";

    public static string Show(string stored) => TryRead(stored) is { } p ? $"{p.X}, {p.Y}" : string.Empty;

    public static (int X, int Y)? TryRead(string stored)
    {
        var parts = stored.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y) ? (x, y) : null;
    }
}
