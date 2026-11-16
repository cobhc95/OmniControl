using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>
/// Reads what is under a screen point through UI Automation (raw COM, since only a few calls are needed): the control type of the
/// element and of its parents. Nothing is kept between calls. Run it on a thread-pool (multithreaded apartment) thread.
/// </summary>
internal static class UiaElements
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

    /// <summary>The control types of the element at the point and up to <paramref name="parentLevels"/> parents; null when it can't be read.</summary>
    public static IReadOnlyList<int>? ControlTypesAt(int x, int y, int parentLevels)
    {
        if (Activator.CreateInstance(Type.GetTypeFromCLSID(AutomationClass)!) is not IUIAutomation automation) return null;
        try
        {
            if (automation.ElementFromPoint(new Pt { X = x, Y = y }, out var element) != 0 || element is null) return null;
            if (automation.ControlViewWalker(out var walker) != 0) return null;
            var types = new List<int>();
            IUIAutomationElement? at = element;
            for (var level = 0; at is not null && level <= parentLevels; level++)
            {
                if (at.GetCurrentPropertyValue(ControlTypePropertyId, out var type) == 0 && type is int id) types.Add(id);
                if (walker.GetParentElement(at, out var parent) != 0) break;
                at = parent;
            }
            return types;
        }
        finally
        {
            Marshal.ReleaseComObject(automation);
        }
    }
}
