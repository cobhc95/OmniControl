using System.ComponentModel;
using OmniControl.App.Services;
using System.Drawing;
using System.Drawing.Imaging;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace OmniControl.App.Audio;

/// <summary>Program icons for the mixer, read from each .exe and cached.</summary>
public static class ProcessIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Get(string exePath)
    {
        if (Cache.TryGetValue(exePath, out var cached)) return cached;
        Bitmap? result = null;
        try
        {
            if (File.Exists(exePath) && Icon.ExtractAssociatedIcon(exePath) is { } icon)
            {
                using var bmp = icon.ToBitmap(); // System.Drawing.Bitmap
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                result = new Bitmap(ms);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception or UnauthorizedAccessException)
        {
            Guard.Log($"Reading the icon of {exePath}", ex); // no icon: the row shows an initial instead
        }
        Cache[exePath] = result;
        return result;
    }
}
