namespace OmniControl.Core.Native;

/// user32 display-mode calls (read a display's current mode, change it) and the DEVMODE they use.
internal static class DisplaySettingsNative
{
    public const int CurrentSettings = -1;          // ENUM_CURRENT_SETTINGS
    public const uint FieldPosition = 0x00000020;    // DM_POSITION
    public const uint FieldOrientation = 0x00000080; // DM_DISPLAYORIENTATION
    public const uint FieldWidth = 0x00080000;       // DM_PELSWIDTH
    public const uint FieldHeight = 0x00100000;      // DM_PELSHEIGHT
    public const uint UpdateRegistry = 0x00000001;   // CDS_UPDATEREGISTRY
    public const int Successful = 0;                 // DISP_CHANGE_SUCCESSFUL

    /// The display flavour of DEVMODEW (the printer fields of its union are not used here).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettingsEx(string deviceName, int modeNum, ref DEVMODE mode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE mode, IntPtr hwnd, uint flags, IntPtr param);
}
