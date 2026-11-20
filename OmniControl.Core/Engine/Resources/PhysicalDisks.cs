using static OmniControl.Core.Native.DeviceIoNative;

namespace OmniControl.Core.Engine.Resources;

/// One physical disk (not a partition or volume): a stable <paramref name="Id"/> (its serial number, else its model and number), Windows'
/// disk <paramref name="Number"/> (\\.\PhysicalDriveN, Disk Management's "Disk N") and its <paramref name="Model"/>.
public sealed record PhysicalDisk(string Id, int Number, string Model);

/// THE list of the PC's physical disks, read from Windows' own storage descriptor (IOCTL_STORAGE_QUERY_PROPERTY on each \\.\PhysicalDriveN,
/// opened without read rights, so no administrator rights and no disk access); virtual disks (VHDs, Storage Spaces) are left out. A few
/// milliseconds: called on a worker at start-up and after Windows announces a device change.
public static unsafe class PhysicalDisks
{
    /// Windows numbers disks from 0 with few gaps; this many names are tried (an absent one fails at once).
    private const int MaxDisks = 64;

    private const uint QueryProperty = 0x2D1400; // IOCTL_STORAGE_QUERY_PROPERTY
    private const int DescriptorSize = 1024; // STORAGE_DEVICE_DESCRIPTOR and its strings
    private const int VendorAt = 12, ProductAt = 16, SerialAt = 24, BusAt = 28; // its offsets
    private const int BusVirtual = 0x0E, BusFileBackedVirtual = 0x0F, BusSpaces = 0x10;

    public static IReadOnlyList<PhysicalDisk> List()
    {
        var disks = new List<PhysicalDisk>();
        var buffer = new byte[DescriptorSize];
        for (var number = 0; number < MaxDisks; number++)
        {
            using var disk = CreateFile($@"\\.\PhysicalDrive{number}", 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (disk.IsInvalid || !Describe(disk, buffer)) continue;
            if (BitConverter.ToInt32(buffer, BusAt) is BusVirtual or BusFileBackedVirtual or BusSpaces) continue;
            var model = Text(buffer, ProductAt) ?? Text(buffer, VendorAt) ?? "Disk";
            var id = Safe(Text(buffer, SerialAt) ?? string.Empty);
            disks.Add(new PhysicalDisk(id.Length > 0 ? id : Safe($"{model}-{number}"), number, model));
        }
        return disks;
    }

    private static bool Describe(Microsoft.Win32.SafeHandles.SafeFileHandle disk, byte[] buffer)
    {
        var query = stackalloc int[3] { 0, 0, 0 }; // STORAGE_PROPERTY_QUERY: StorageDeviceProperty, PropertyStandardQuery, one spare byte
        Array.Clear(buffer);
        fixed (byte* output = buffer)
            return DeviceIoControl(disk, QueryProperty, query, 12, output, buffer.Length, out _, IntPtr.Zero);
    }

    /// The trimmed ASCII string the descriptor's offset at <paramref name="field"/> points to, or null when there is none.
    private static string? Text(byte[] buffer, int field)
    {
        var at = BitConverter.ToInt32(buffer, field);
        if (at <= 0 || at >= buffer.Length) return null;
        var end = Array.IndexOf(buffer, (byte)0, at);
        var text = Encoding.ASCII.GetString(buffer, at, (end < 0 ? buffer.Length : end) - at).Trim();
        return text.Length > 0 ? text : null;
    }

    /// Letters, digits, '-' and '_' only, so the id sits in a state's or setting's id as it is.
    private static string Safe(string text) => new(text.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
}
