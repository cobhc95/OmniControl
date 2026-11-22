namespace OmniControl.Core.Engine;

/// Where the run of the taskbar's own buttons that starts at its left (top) end stops, read through UI Automation (raw COM, one cached
/// FindAll): Windows 11's taskbar is XAML, its Start, Widgets and program buttons have no windows of their own. Centred icons leave only the
/// Widgets button there (or nothing); left-aligned icons run from Start through the last pinned or running program. A worker (MTA) thread
/// only, never the UI or hook thread; every COM object is released before it returns, so nothing is held in Explorer.
public static class TaskbarButtons
{
    private const int BoundsId = 30001, ProcessId = 30002, OffscreenId = 30022, Descendants = 4;
    // Buttons of one run sit a few pixels apart and the first a little in from the edge; the gap before centred icons is far wider.
    private const double BaseDpi = 96, TouchGapDips = 24;
    private static readonly Guid AutomationClass = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
        [PreserveSig] int ElementFromHandle(IntPtr window, out IUIAutomationElement? element);
        void ElementFromPoint(); void GetFocusedElement(); void GetRootElementBuildCache(); void ElementFromHandleBuildCache(); void ElementFromPointBuildCache();
        void GetFocusedElementBuildCache(); void CreateTreeWalker(); void ControlViewWalker(); void ContentViewWalker(); void RawViewWalker(); void RawViewCondition();
        [PreserveSig] int ControlViewCondition(out IUIAutomationCondition? condition);
        void ContentViewCondition();
        [PreserveSig] int CreateCacheRequest(out IUIAutomationCacheRequest? request);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache();
        [PreserveSig] int FindAllBuildCache(int scope, IUIAutomationCondition condition, IUIAutomationCacheRequest request, out IUIAutomationElementArray? found);
        void BuildUpdatedCache(); void GetCurrentPropertyValue(); void GetCurrentPropertyValueEx();
        [PreserveSig] int GetCachedPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition
    {
    }

    [ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCacheRequest
    {
        [PreserveSig] int AddProperty(int propertyId);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        [PreserveSig] int Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationElement? element);
    }

    /// The screen position (physical pixels, along the taskbar) where the run of buttons touching the taskbar's left (top) end stops; the
    /// taskbar's own left (top) edge when no button touches it; null when the taskbar or UI Automation cannot be read. Elements wider than a
    /// third of the taskbar are its containers and left out, so are OmniControl's own.
    public static int? RunEnd(IntPtr taskbar)
    {
        if (!GetWindowRect(taskbar, out var bar)) return null;
        var across = bar.Bottom - bar.Top > bar.Right - bar.Left;
        var (start, length) = across ? (bar.Top, bar.Bottom - bar.Top) : (bar.Left, bar.Right - bar.Left);
        var spans = Spans(taskbar) ?? new List<Span>();
        spans.AddRange(WindowSpans(taskbar, bar));
        if (spans.Count == 0) return null; // nothing readable (Explorer redrawing, an auto-hidden taskbar's XAML off screen): keep the last end
        var touch = TouchGapDips * GetDpiForWindow(taskbar) / BaseDpi;
        var end = (double)start;
        foreach (var (from, to) in spans.Select(s => across ? (s.Top, s.Bottom) : (s.Left, s.Right)).Where(s => s.Item2 - s.Item1 < length / 3.0).OrderBy(s => s.Item1))
        {
            if (from > end + touch) break; // a gap: the centred icons, or the free middle of the taskbar
            end = Math.Max(end, to);
        }
        return (int)Math.Round(end);
    }

    private readonly record struct Span(double Left, double Top, double Right, double Bottom);

    /// The bounds of every on-screen control element inside the taskbar but OmniControl's own; null when UI Automation fails.
    private static List<Span>? Spans(IntPtr taskbar)
    {
        IUIAutomation? automation = null;
        IUIAutomationElement? root = null;
        IUIAutomationCondition? condition = null;
        IUIAutomationCacheRequest? request = null;
        IUIAutomationElementArray? found = null;
        try
        {
            automation = Activator.CreateInstance(Type.GetTypeFromCLSID(AutomationClass)!) as IUIAutomation;
            if (automation is null || automation.ElementFromHandle(taskbar, out root) != 0 || root is null
                || automation.ControlViewCondition(out condition) != 0 || condition is null
                || automation.CreateCacheRequest(out request) != 0 || request is null) return null;
            foreach (var id in new[] { BoundsId, ProcessId, OffscreenId }) request.AddProperty(id);
            if (root.FindAllBuildCache(Descendants, condition, request, out found) != 0 || found is null || found.Length(out var count) != 0) return null;
            var own = Environment.ProcessId;
            var spans = new List<Span>();
            for (var i = 0; i < count; i++)
            {
                if (found.GetElement(i, out var element) != 0 || element is null) continue;
                try
                {
                    if (Cached(element, ProcessId) is int pid && pid == own) continue;
                    if (Cached(element, OffscreenId) is true) continue;
                    if (Cached(element, BoundsId) is double[] { Length: 4 } r && r[2] > 0 && r[3] > 0) spans.Add(new Span(r[0], r[1], r[0] + r[2], r[1] + r[3]));
                }
                finally { Release(element); }
            }
            return spans;
        }
        catch (COMException)
        {
            return null; // Explorer restarting or the taskbar gone mid-read: the next placement trigger reads again
        }
        finally
        {
            Release(found);
            Release(request);
            Release(condition);
            Release(root);
            Release(automation);
        }
    }

    /// The visible windows over the taskbar but OmniControl's own: its child windows (the classic Start button of ExplorerPatcher or
    /// StartAllBack) and Explorer's top-level windows that overlap it (StartAllBack's orb is one, taller than the taskbar, unseen by UI Automation).
    private static List<Span> WindowSpans(IntPtr taskbar, Rect bar)
    {
        var own = (uint)Environment.ProcessId;
        GetWindowThreadProcessId(taskbar, out var explorer);
        var thick = 2 * Math.Min(bar.Right - bar.Left, bar.Bottom - bar.Top); // an orb, never a folder window dragged over the taskbar
        var spans = new List<Span>();
        void Add(IntPtr window, bool child)
        {
            if (window == taskbar || !IsWindowVisible(window) || !GetWindowRect(window, out var r) || r.Right <= r.Left || r.Bottom <= r.Top) return;
            GetWindowThreadProcessId(window, out var pid);
            if (pid == own || (!child && (pid != explorer || r.Right <= bar.Left || r.Left >= bar.Right || r.Bottom <= bar.Top || r.Top >= bar.Bottom || r.Right - r.Left > thick || r.Bottom - r.Top > thick))) return;
            spans.Add(new Span(r.Left, r.Top, r.Right, r.Bottom));
        }
        EnumChildWindows(taskbar, (w, _) => { Add(w, true); return true; }, IntPtr.Zero);
        EnumWindows((w, _) => { Add(w, false); return true; }, IntPtr.Zero);
        return spans;
    }

    private delegate bool WindowVisit(IntPtr window, IntPtr data);

    private static object? Cached(IUIAutomationElement element, int propertyId) =>
        element.GetCachedPropertyValue(propertyId, out var value) == 0 ? value : null;

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowVisit visit, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisit visit, IntPtr data);
}
