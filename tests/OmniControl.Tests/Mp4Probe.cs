using System.Runtime.InteropServices;

namespace OmniControl.Tests;

/// Reads an MP4 back through Media Foundation's Source Reader: its duration and video frame count.
internal static class Mp4Probe
{
    private const int MediaSource = unchecked((int)0xFFFFFFFF), AllStreams = unchecked((int)0xFFFFFFFE), FirstVideo = unchecked((int)0xFFFFFFFC);
    private const int EndOfStream = 0x2;
    private static readonly Guid PresentationDuration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    public static (TimeSpan Duration, int Frames) Read(string path)
    {
        Marshal.ThrowExceptionForHR(MFStartup(0x00020070, 0));
        try
        {
            Marshal.ThrowExceptionForHR(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out var reader));
            try
            {
                var variant = Marshal.AllocHGlobal(32);
                try
                {
                    var key = PresentationDuration;
                    Marshal.ThrowExceptionForHR(reader.GetPresentationAttribute(MediaSource, ref key, variant));
                    var duration = TimeSpan.FromTicks(Marshal.ReadInt64(variant, 8));
                    Marshal.ThrowExceptionForHR(reader.SetStreamSelection(AllStreams, 0));
                    Marshal.ThrowExceptionForHR(reader.SetStreamSelection(FirstVideo, 1));
                    var frames = 0;
                    while (true)
                    {
                        Marshal.ThrowExceptionForHR(reader.ReadSample(FirstVideo, 0, out _, out var flags, out _, out var sample));
                        if (sample != IntPtr.Zero)
                        {
                            frames++;
                            Marshal.Release(sample);
                        }
                        if ((flags & EndOfStream) != 0) break;
                    }
                    return (duration, frames);
                }
                finally
                {
                    Marshal.FreeHGlobal(variant);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }
        finally
        {
            MFShutdown();
        }
    }

    [DllImport("mfplat.dll")] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int stream, out int selected);
        [PreserveSig] int SetStreamSelection(int stream, int selected);
        [PreserveSig] int GetNativeMediaType(int stream, int index, out IntPtr type);
        [PreserveSig] int GetCurrentMediaType(int stream, out IntPtr type);
        [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IntPtr type);
        [PreserveSig] int SetCurrentPosition(ref Guid format, IntPtr position);
        [PreserveSig] int ReadSample(int stream, int control, out int actualStream, out int flags, out long timestamp, out IntPtr sample);
        [PreserveSig] int Flush(int stream);
        [PreserveSig] int GetServiceForStream(int stream, ref Guid service, ref Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(int stream, ref Guid attribute, IntPtr value);
    }
}
