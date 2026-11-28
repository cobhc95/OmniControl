using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Shared.Services;

/// Program icons read from each .exe and kept for the last <see cref="Timing.ProcessIconCacheMax"/> programs asked for
/// (one cache per owner, UI thread). A picture that leaves the cache is not disposed: a row may still show it.
public sealed class ProcessIcons
{
    private static readonly Vector IconDpi = new(96, 96);

    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    /// The program's icon; null when it has none (the row shows an initial instead).
    public Bitmap? Get(string exePath) => _cache.TryGetValue(exePath, out var cached) ? cached : Keep(exePath, Read(exePath));

    /// The same, the file read on a worker so the UI thread never waits for the disk.
    public async Task<Bitmap?> GetAsync(string exePath)
    {
        if (_cache.TryGetValue(exePath, out var cached)) return cached;
        var icon = await Task.Run(() => Read(exePath));
        return _cache.TryGetValue(exePath, out cached) ? cached : Keep(exePath, icon);
    }

    private static ScreenImage? Read(string exePath) => File.Exists(exePath) ? ProgramFile.IconOf(exePath) : null;

    /// Drops the cached bitmaps (called when the main window is released so they can be collected; a row still showing one keeps its own reference).
    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
    }

    private Bitmap? Keep(string exePath, ScreenImage? icon)
    {
        var result = icon is null ? null : ToBitmap(icon);
        _cache[exePath] = result;
        _order.Enqueue(exePath);
        while (_order.Count > Timing.ProcessIconCacheMax) _cache.Remove(_order.Dequeue());
        return result;
    }

    private static Bitmap ToBitmap(ScreenImage icon)
    {
        var pin = GCHandle.Alloc(icon.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, pin.AddrOfPinnedObject(), new PixelSize(icon.Width, icon.Height), IconDpi, icon.Stride);
        }
        finally
        {
            pin.Free();
        }
    }
}
