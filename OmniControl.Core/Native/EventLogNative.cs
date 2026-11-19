namespace OmniControl.Core.Native;

/// wevtapi: reading events from a Windows event log by an XPath query, each rendered as its XML.
internal static class EventLogNative
{
    public const int QueryChannelPath = 0x1, QueryReverseDirection = 0x200, RenderEventXml = 1, InsufficientBuffer = 122, NoMoreItems = 259;

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr EvtQuery(IntPtr session, string path, string query, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    public static extern bool EvtNext(IntPtr resultSet, int eventArraySize, [Out] IntPtr[] eventArray, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll", SetLastError = true)]
    public static extern bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", SetLastError = true)]
    public static extern bool EvtClose(IntPtr handle);
}
