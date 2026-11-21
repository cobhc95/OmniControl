using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine;

/// Whether a file in a folder others can write (Equalizer APO's config folder) may be written or deleted as itself: not a symbolic link,
/// junction or other reparse point, not a hard link to a file elsewhere, and its folder not a reparse point either; otherwise a write
/// by an elevated OmniControl could land on a system file.
public static class PlainFile
{
    // BY_HANDLE_FILE_INFORMATION: its FILETIMEs are two DWORDs, so 4-byte packing keeps the offsets.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct FileInformation
    {
        public uint Attributes;
        public long Created, Accessed, Written;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    /// True when <paramref name="path"/> is plain (a missing file is, when its folder is). Throws what a file read throws.
    public static bool IsPlain(string path)
    {
        if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
        if (!File.Exists(path)) return true;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(handle, out var information) && information.NumberOfLinks <= 1;
    }
}
