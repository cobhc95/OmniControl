namespace OmniControl.Core.Native;

/// comdlg32 and gdi32 calls that show Windows' print dialog and print one picture on the printer chosen there.
internal static class PrintNative
{
    /// PRINTDLGW with the 64-bit layout (natural alignment).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PrintDlg
    {
        public int StructSize;
        public IntPtr Owner, DevMode, DevNames, Hdc;
        public uint Flags;
        public ushort FromPage, ToPage, MinPage, MaxPage, Copies;
        public IntPtr Instance, CustData, PrintHook, SetupHook, PrintTemplateName, SetupTemplateName, PrintTemplate, SetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DocInfo
    {
        public int Size;
        public string DocName;
        public string? Output, DataType;
        public uint Type;
    }

    public const uint PdReturnDc = 0x100, PdNoSelection = 0x4, PdNoPageNums = 0x8, PdUseDevModeCopiesAndCollate = 0x40000;
    public const int HorzRes = 8, VertRes = 10, LogPixelsX = 88, LogPixelsY = 90, Halftone = 4;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "PrintDlgW")] public static extern bool ShowPrintDlg(ref PrintDlg dialog);
    [DllImport("comdlg32.dll")] public static extern uint CommDlgExtendedError();
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartDocW")] public static extern int StartDoc(IntPtr hdc, ref DocInfo info);
    [DllImport("gdi32.dll")] public static extern int StartPage(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern int EndPage(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern int EndDoc(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern int AbortDoc(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] public static extern int StretchDIBits(IntPtr hdc, int x, int y, int width, int height, int sx, int sy, int sWidth, int sHeight,
        byte[] bits, ref ScreenshotNative.BitmapInfoHeader info, uint usage, int rop);
}
