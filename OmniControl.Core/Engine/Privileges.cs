namespace OmniControl.Core.Engine;

/// Switches on a privilege the process's token holds switched off (Windows' calls that need one fail until it is on).
internal static class Privileges
{
    public const string Shutdown = "SeShutdownPrivilege", TakeOwnership = "SeTakeOwnershipPrivilege";

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, int length, IntPtr previous, IntPtr returnLength);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    // TOKEN_PRIVILEGES with one entry: count, LUID, attributes.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8;
    private const int PrivilegeEnabled = 2;

    /// Best effort: when it can't be switched on, the call that needs it fails and its caller reports that. <paramref name="on"/> false
    /// switches it off again.
    public static void Enable(string name, bool on = true)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return;
            var state = new TokenPrivilege { Count = 1, Luid = luid, Attributes = on ? PrivilegeEnabled : 0 };
            AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
