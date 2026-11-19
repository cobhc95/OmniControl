namespace OmniControl.Core.Engine.Mouse;

/// Reads what is under a screen point through UI Automation (raw COM, since only a few calls are needed): the control type of the
/// element and of its parents. One instance per worker thread (a multithreaded-apartment thread, never the UI or hook thread) keeps one
/// automation object for its life; every element and walker a lookup gets is released at once, so nothing is held in the other app.
internal sealed class UiaElements : IDisposable
{
    private const int ControlTypePropertyId = 30003;
    private static readonly Guid AutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }

    // Only the slots up to the calls used are named; the rest keep the vtable order.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement(); void ElementFromHandle();
        [PreserveSig] int ElementFromPoint(Pt point, out IUIAutomationElement element);
        void GetFocusedElement(); void GetRootElementBuildCache(); void ElementFromHandleBuildCache(); void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache(); void CreateTreeWalker();
        [PreserveSig] int ControlViewWalker(out IUIAutomationTreeWalker walker);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object value);
    }

    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTreeWalker
    {
        [PreserveSig] int GetParentElement(IUIAutomationElement element, out IUIAutomationElement? parent);
    }

    private IUIAutomation? _automation;

    /// The control types of the element at the point and up to <paramref name="parentLevels"/> parents; null when it can't be read.
    public IReadOnlyList<int>? ControlTypesAt(int x, int y, int parentLevels)
    {
        _automation ??= Activator.CreateInstance(Type.GetTypeFromCLSID(AutomationClass)!) as IUIAutomation;
        if (_automation is null) return null;
        if (_automation.ElementFromPoint(new Pt { X = x, Y = y }, out var element) != 0 || element is null) return null;
        IUIAutomationTreeWalker? walker = null;
        IUIAutomationElement? at = element;
        try
        {
            if (_automation.ControlViewWalker(out walker) != 0) return null;
            var types = new List<int>();
            for (var level = 0; at is not null && level <= parentLevels; level++)
            {
                if (at.GetCurrentPropertyValue(ControlTypePropertyId, out var type) == 0 && type is int id) types.Add(id);
                var failed = walker.GetParentElement(at, out var parent) != 0;
                var done = at;
                at = failed ? null : parent;
                Release(done);
            }
            return types;
        }
        finally
        {
            Release(at);
            Release(walker);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    public void Dispose()
    {
        Release(_automation);
        _automation = null;
    }
}
