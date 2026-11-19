using OmniControl.Core.Engine.Screenshot;
using static OmniControl.Core.Native.IconNative;
using static OmniControl.Core.Native.ScreenshotNative;
using static OmniControl.Core.Native.VideoNative;

namespace OmniControl.Core.Engine;

/// What a program's .exe says about itself (reads the file only).
public static class ProgramFile
{
    /// The name a person knows the program by: the file's description ("Google Chrome"), else its product name, else its file name without ".exe", capitalised.
    public static string NameOf(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (new[] { info.FileDescription, info.ProductName }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) is { } d) return d.Trim();
        }
        catch (FileNotFoundException) { /* the file is gone or never existed: use its name */ }
        catch (IOException) { /* locked or unreadable: use its name */ }
        catch (UnauthorizedAccessException) { /* not allowed to read it: use its name */ }
        var n = Path.GetFileNameWithoutExtension(path);
        return n.Length == 0 ? path : char.ToUpperInvariant(n[0]) + n[1..];
    }

    /// The icon Explorer shows for the file, as BGRA pixels with straight (not premultiplied) alpha; null when Windows gives none.
    /// Every handle Windows hands out is released before it returns.
    public static ScreenImage? IconOf(string path)
    {
        if (path.Length >= MaxPath) return null;
        var buffer = new char[MaxPath]; // Windows may write the path of the file the icon came from into it
        path.CopyTo(0, buffer, 0, path.Length);
        ushort index = 0;
        var icon = ExtractAssociatedIcon(IntPtr.Zero, buffer, ref index);
        if (icon == IntPtr.Zero) return null;
        try
        {
            if (!GetIconInfo(icon, out var info)) return null;
            try
            {
                return info.Color == IntPtr.Zero ? null : Pixels(info.Color, info.Mask);
            }
            finally
            {
                if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
                if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// The colour picture's pixels; an old icon without alpha takes its see-through parts from the mask (white = see-through).
    private static ScreenImage? Pixels(IntPtr color, IntPtr mask)
    {
        if (GetObject(color, Marshal.SizeOf<BitmapFacts>(), out var facts) == 0 || facts.Width <= 0 || facts.Height <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var pixels = Read(screen, color, facts.Width, facts.Height);
            if (pixels is null) return null;
            if (!HasAlpha(pixels) && mask != IntPtr.Zero && Read(screen, mask, facts.Width, facts.Height) is { } cut)
                for (var at = 3; at < pixels.Length; at += ScreenImage.BytesPerPixel) pixels[at] = cut[at - 1] == 0 ? byte.MaxValue : byte.MinValue;
            return new ScreenImage(facts.Width, facts.Height, pixels);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// A bitmap's pixels as 32-bit BGRA, top row first; null when Windows refuses.
    private static byte[]? Read(IntPtr hdc, IntPtr bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bits = new byte[width * height * ScreenImage.BytesPerPixel];
        return GetDIBits(hdc, bitmap, 0, (uint)height, bits, ref header, DibRgbColors) > 0 ? bits : null;
    }

    private static bool HasAlpha(byte[] bgra)
    {
        for (var at = 3; at < bgra.Length; at += ScreenImage.BytesPerPixel)
            if (bgra[at] != 0) return true;
        return false;
    }
}
