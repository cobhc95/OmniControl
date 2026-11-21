using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine;

/// Whether a file in a folder others can write (Equalizer APO's config folder) may be written or deleted as itself: not a symbolic link,
/// junction or other reparse point, not a hard link to a file elsewhere, and its folder not a reparse point either; otherwise a write
/// by an elevated OmniControl could land on a system file. <see cref="OpenToWrite"/> checks the very handle it writes through, so a file
/// swapped for a link after a check is never followed.
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

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenAlways = 4, OpenReparsePoint = 0x00200000, ReparseAttribute = 0x400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);

    /// True when <paramref name="path"/> is plain (a missing file is, when its folder is). Throws what a file read throws.
    public static bool IsPlain(string path)
    {
        if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return false;
        if (!File.Exists(path)) return true;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return GetFileInformationByHandle(handle, out var information) && information.NumberOfLinks <= 1;
    }

    /// Opens (or creates) <paramref name="path"/> itself, never what a link points to, checks through that handle that it is a plain file,
    /// and empties it: the stream then writes the new content. An <see cref="IOException"/> carries Windows' error (a sharing violation
    /// included) or says the file is a link.
    public static FileStream OpenToWrite(string path, FileShare share)
    {
        var handle = CreateFile(path, GenericRead | GenericWrite, share, IntPtr.Zero, OpenAlways, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException(new Win32Exception(error).Message, unchecked((int)0x80070000) | error);
        }
        if (!GetFileInformationByHandle(handle, out var information) || (information.Attributes & ReparseAttribute) != 0 || information.NumberOfLinks > 1)
        {
            handle.Dispose();
            throw new IOException($"{Path.GetFileName(path)} is a link, so OmniControl left it alone");
        }
        var stream = new FileStream(handle, FileAccess.ReadWrite);
        try
        {
            stream.SetLength(0);
            return stream;
        }
        catch (IOException)
        {
            stream.Dispose();
            throw;
        }
    }
}
