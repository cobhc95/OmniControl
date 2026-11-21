namespace OmniControl.Core.Native;

/// wtsapi32: the notice Windows sends a window when the user's session connects, disconnects, logs on or unlocks (Remote Desktop included),
/// and the user signed in to a session.
internal static class SessionNative
{
    public const uint WmWtsSessionChange = 0x02B1;
    public const int NotifyForThisSession = 0;

    /// The <c>wParam</c> values of <see cref="WmWtsSessionChange"/> that can change which monitors the session sees: console connect (back at
    /// the PC after Remote Desktop), remote connect, remote disconnect, logon, unlock.
    public const int WtsConsoleConnect = 1, WtsRemoteConnect = 3, WtsRemoteDisconnect = 4, WtsSessionLogon = 5, WtsSessionUnlock = 8;

    /// The other <c>wParam</c> values the power journal records (they change no monitor): log off and lock.
    public const int WtsSessionLogoff = 6, WtsSessionLock = 7;

    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    /// <c>WTSQuerySessionInformationW</c>'s this process's session and the user and domain name it reads.
    public const int CurrentSession = -1, InfoUserName = 5, InfoDomainName = 7;

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr memory);
}
