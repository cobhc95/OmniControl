using SkiaSharp;

namespace OmniControl.Screenshots;

/// Compares two folders of shots pixel by pixel (before and after a change to the master styles) and lists each PNG that differs: its
/// size, or how many pixels differ and the box around them. Returns the number of shots that differ.
internal static class ShotCompare
{
    public static int Run(string before, string after)
    {
        var differ = 0;
        foreach (var path in Directory.GetFiles(before, "*.png").Order())
        {
            var name = Path.GetFileName(path);
            var other = Path.Combine(after, name);
            if (!File.Exists(other)) { Console.WriteLine($"{name}: missing after"); differ++; continue; }
            using var a = SKBitmap.Decode(path);
            using var b = SKBitmap.Decode(other);
            if (a.Width != b.Width || a.Height != b.Height) { Console.WriteLine($"{name}: size {a.Width}x{a.Height} -> {b.Width}x{b.Height}"); differ++; continue; }
            int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) == b.GetPixel(x, y)) continue;
                count++;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            if (count == 0) continue;
            differ++;
            Console.WriteLine($"{name}: {count} pixels differ in ({left},{top})-({right},{bottom})");
        }
        Console.WriteLine(differ == 0 ? "No shot differs." : $"{differ} shot(s) differ.");
        return differ;
    }
}
