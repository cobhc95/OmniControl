using System.Runtime.InteropServices;
using System.Text;
using static OmniControl.Core.Native.ScreenshotNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// Puts a picture (and, when asked, the file it was saved to) or a text on the Windows clipboard. The picture goes on as a DIB (every
/// program) and, in the screenshot's format, as JPG ("JFIF", for the programs that take JPG from the clipboard) or else as PNG (browsers,
/// Office); a file path goes on as a file (paste in Explorer) and as text. Memory the clipboard does not take is freed here; the clipboard
/// owns the rest.
/// </summary>
public static class ImageClipboard
{
    private const int DropFilesSize = 20; // DROPFILES: offset, point, non-client flag, wide flag
    private static readonly Lazy<uint> PngFormat = new(() => RegisterClipboardFormat("PNG")), JpgFormat = new(() => RegisterClipboardFormat("JFIF"));

    /// <summary>
    /// Puts the picture on the clipboard: always as a DIB, plus JPG at <paramref name="quality"/> when <paramref name="format"/> is JPG and PNG
    /// otherwise (WebP and BMP have no clipboard format programs read); with <paramref name="filePath"/> as a file and as text when given.
    /// False when the clipboard stayed busy.
    /// </summary>
    public static bool SetImage(ScreenImage image, ImageFormat format, int quality, string? filePath = null)
    {
        var items = new List<(uint Format, byte[] Data)>
        {
            (CfDib, Dib(image)),
            format == ImageFormat.Jpg
                ? (JpgFormat.Value, ImageEncoder.Encode(image, ImageFormat.Jpg, quality))
                : (PngFormat.Value, ImageEncoder.Encode(image, ImageFormat.Png, ImageFormats.MaxQuality)),
        };
        if (filePath is { Length: > 0 })
        {
            items.Add((CfHDrop, DropFiles(filePath)));
            items.Add((CfUnicodeText, Encoding.Unicode.GetBytes(filePath + '\0')));
        }
        return Put(items);
    }

    /// <summary>Puts plain text on the clipboard. False when the clipboard stayed busy.</summary>
    public static bool SetText(string text) => Put(new List<(uint, byte[])> { (CfUnicodeText, Encoding.Unicode.GetBytes(text + '\0')) });

    private static bool Put(List<(uint Format, byte[] Data)> items)
    {
        for (var i = 0; i < CoreTiming.ClipboardAttempts; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    foreach (var (format, data) in items) SetOne(format, data);
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(CoreTiming.ClipboardWaitMs);
        }
        return false;
    }

    private static void SetOne(uint format, byte[] data)
    {
        var memory = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
        if (memory == IntPtr.Zero) return;
        var target = GlobalLock(memory);
        if (target == IntPtr.Zero)
        {
            GlobalFree(memory);
            return;
        }
        Marshal.Copy(data, 0, target, data.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == IntPtr.Zero) GlobalFree(memory); // taken: the clipboard frees it later
    }

    /// <summary>A packed DIB: the 40-byte header, then 32-bit pixels bottom row first.</summary>
    internal static byte[] Dib(ScreenImage image)
    {
        var header = Marshal.SizeOf<BitmapInfoHeader>();
        var bytes = new byte[header + image.Pixels.Length];
        using (var writer = new BinaryWriter(new MemoryStream(bytes)))
        {
            writer.Write(header);
            writer.Write(image.Width);
            writer.Write(image.Height); // positive: bottom row first, what most programs expect
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0); // BI_RGB
            writer.Write(image.Pixels.Length);
        }
        for (var y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Pixels, y * image.Stride, bytes, header + (image.Height - 1 - y) * image.Stride, image.Stride);
        return bytes;
    }

    private static byte[] DropFiles(string path)
    {
        var names = Encoding.Unicode.GetBytes(path + "\0\0");
        var bytes = new byte[DropFilesSize + names.Length];
        BitConverter.GetBytes(DropFilesSize).CopyTo(bytes, 0); // where the names start
        BitConverter.GetBytes(1).CopyTo(bytes, 16); // the names are wide characters
        names.CopyTo(bytes, DropFilesSize);
        return bytes;
    }
}
