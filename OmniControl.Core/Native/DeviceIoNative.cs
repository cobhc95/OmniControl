using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Native;

/// Opening a device by name and sending it a control code (the packet filter's driver, the physical disks).
internal static unsafe class DeviceIoNative
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    public static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, IntPtr overlapped);
}
