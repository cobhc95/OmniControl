using System.Reflection;

namespace OmniControl.Core.Engine;

/// Opens a web or Settings address through Explorer itself (the desktop's IShellDispatch2.ShellExecute, the documented way for an elevated
/// program), so the browser starts with the signed-in user's normal rights and in their session, with no second copy of OmniControl needed.
public static class ShellDesktop
{
    private const int DesktopClass = 8, NeedDispatch = 1, ActiveView = 0, ShowNormal = 1, DesktopFolder = 0; // SWC_DESKTOP, SWFO_NEEDDISPATCH, SVGIO_BACKGROUND, SW_SHOWNORMAL, CSIDL_DESKTOP
    private static readonly Guid ShellWindowsClass = new("9ba05972-f6a8-11cf-a442-00a0c90a8f39"), TopLevelBrowser = new("4c96be40-915c-11cf-99d3-00aa004ae837"),
        ShellBrowserId = new("000214e2-0000-0000-c000-000000000046"), DispatchId = new("00020400-0000-0000-c000-000000000046");

    [ComImport, Guid("85cb6900-4d95-11cf-960c-0080c7f4ee85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void Count(); void Item(); void NewEnum(); void Register(); void RegisterPending(); void Revoke(); void OnNavigate(); void OnActivated();
        [PreserveSig] int FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int window, int options, [MarshalAs(UnmanagedType.IDispatch)] out object? dispatch);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object? found);
    }

    [ComImport, Guid("000214e2-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB(); void EnableModelessSB();
        void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? view);
    }

    [ComImport, Guid("000214e3-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate(); void Refresh(); void CreateViewWindow();
        void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
        [PreserveSig] int GetItemObject(int item, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object? found);
    }

    /// True when Explorer opened <paramref name="address"/> (https:, http: or ms-settings: only); false when it is another kind of target,
    /// Explorer could not be reached or did not answer within <see cref="CoreTiming.ShellOpenWait"/>, so the caller can fall back. The shell's
    /// COM objects need an apartment of their own, so the work runs on a short-lived STA thread; the caller waits (never call it on the UI thread).
    public static bool TryOpenAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "ms-settings")) return false;
        var opened = false;
        var worker = new Thread(() => opened = OpenInShell(uri)) { IsBackground = true, Name = "Open address" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return worker.Join(CoreTiming.ShellOpenWait) && opened;
    }

    private static bool OpenInShell(Uri uri)
    {
        var held = new List<object>();
        try
        {
            var windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClass) ?? throw new InvalidOperationException("Windows' shell windows are missing."))!;
            held.Add(windows);
            object? location = DesktopFolder, root = null; // VT_I4 CSIDL_DESKTOP and VT_EMPTY, as the shell's own callers pass them
            if (windows.FindWindowSW(ref location, ref root, DesktopClass, out _, NeedDispatch, out var desktop) != 0 || desktop is null) return false;
            held.Add(desktop);
            var (service, browserId, dispatchId) = (TopLevelBrowser, ShellBrowserId, DispatchId);
            if (((IServiceProvider)desktop).QueryService(ref service, ref browserId, out var browser) != 0 || browser is null) return false;
            held.Add(browser);
            if (((IShellBrowser)browser).QueryActiveShellView(out var view) != 0 || view is null) return false;
            held.Add(view);
            if (((IShellView)view).GetItemObject(ActiveView, ref dispatchId, out var folderView) != 0 || folderView is null) return false;
            held.Add(folderView);
            var application = Get(folderView, "Application");
            held.Add(application);
            application.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application, new object[] { uri.OriginalString, string.Empty, string.Empty, "open", ShowNormal });
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TargetInvocationException or InvalidOperationException or MissingMemberException)
        {
            return false;
        }
        finally
        {
            foreach (var com in held) if (Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
    }

    private static object Get(object com, string property) =>
        com.GetType().InvokeMember(property, BindingFlags.GetProperty, null, com, null) ?? throw new InvalidOperationException($"Explorer gave no {property}.");
}
