namespace OmniControl.Core.Native;

/// shell32, user32 and gdi32 calls that read a program's icon as pixels.
internal static class IconNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapFacts
    {
        public int Type, Width, Height, WidthBytes;
        public short Planes, BitsPixel;
        public IntPtr Bits;
    }

    public const int MaxPath = 260;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ExtractAssociatedIcon(IntPtr instance, char[] path, ref ushort index);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr obj, int size, out BitmapFacts facts);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref ScreenshotNative.BitmapInfoHeader info, uint usage);
}
