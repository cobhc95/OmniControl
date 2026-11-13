using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>The Windows Magnification API's full-screen colour effect (one 5x5 colour matrix for the whole desktop).</summary>
internal static class MagnificationNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MAGCOLOREFFECT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] transform;
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagInitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagUninitialize();

    [DllImport("Magnification.dll", SetLastError = true)]
    public static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);
}
