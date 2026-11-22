namespace OmniControl.Core.Engine;

/// Where a gadget of a given size sits on the taskbar (physical pixels, screen), and the taskbar window it is put into.
public sealed record TaskbarPlace(IntPtr Taskbar, int X, int Y);

/// THE placement of a small window over the Windows taskbar (Windows 11 has no taskbar toolbars): left of the tray icons, or after the buttons
/// at the taskbar's left end, centred in the taskbar's height. <see cref="Attach"/> makes it a child of the taskbar, so it is drawn as part of the taskbar
/// and moves with it into whatever layer Windows 11 lifts the taskbar to (an owned or topmost window is covered then). Explorer restarting
/// destroys the old taskbar (a new <see cref="TaskbarPlace.Taskbar"/>) and its children: the owner makes its window anew and attaches it.
public static class TaskbarSpot
{
    private const double BaseDpi = 96;
    private const string TaskbarClass = "Shell_TrayWnd";

    /// The place for a <paramref name="width"/> x <paramref name="height"/> window, <paramref name="gap"/> device-independent pixels left of the tray (negative: over the tray's own
    /// padding by that much), kept inside the taskbar; on a taskbar at the left or right edge (Windows 10) above the tray, centred in its width
    /// (from its left end when wider). With <paramref name="after"/> (a screen position along the taskbar, <see cref="TaskbarButtons.RunEnd"/>)
    /// it sits <paramref name="gap"/> after that, at the taskbar's left (top) end, instead. Null when there is no taskbar (Explorer is not running).
    public static TaskbarPlace? Find(int width, int height, double gap, int? after = null)
    {
        var bar = Main;
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out var b)) return null;
        var tray = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        var t = default(Rect);
        var hasTray = tray != IntPtr.Zero && GetWindowRect(tray, out t) && t.Right > t.Left && t.Bottom > t.Top;
        var space = (int)Math.Round(gap * GetDpiForWindow(bar) / BaseDpi);
        if (b.Bottom - b.Top > b.Right - b.Left)
        {
            var y = Math.Clamp(after is { } top ? top + space : (hasTray ? t.Top : b.Bottom) - height - space, b.Top, Math.Max(b.Top, b.Bottom - height));
            return new TaskbarPlace(bar, Math.Max(b.Left, b.Left + (b.Right - b.Left - width) / 2), y);
        }
        var x = Math.Clamp(after is { } left ? left + space : (hasTray ? t.Left : b.Right) - width - space, b.Left, Math.Max(b.Left, b.Right - width));
        return new TaskbarPlace(bar, x, b.Top + (b.Bottom - b.Top - height) / 2);
    }

    /// True while Explorer's taskbar exists (false before Explorer has started, or while it restarts).
    public static bool Exists => Main != IntPtr.Zero;

    /// The main taskbar's window (zero while there is none).
    public static IntPtr Main => FindWindow(TaskbarClass, null);

    /// True once <paramref name="window"/> no longer exists (the taskbar that held it was destroyed with it).
    public static bool IsGone(IntPtr window) => !IsWindow(window);

    /// Makes <paramref name="window"/> a child of the taskbar (true); where Windows refuses that, the taskbar owns it instead (false: it is
    /// not hidden with the taskbar, see <see cref="FullScreenOn"/>). Either way it never takes focus, stays out of Alt+Tab and is layered:
    /// it shows only what <see cref="Paint"/> gives it.
    public static bool Attach(IntPtr window, IntPtr taskbar)
    {
        var style = GetWindowLongPtr(window, StyleIndex).ToInt64();
        SetWindowLongPtr(window, StyleIndex, new IntPtr((style & ~Popup) | Child));
        var child = SetParent(window, taskbar) != IntPtr.Zero;
        if (!child)
        {
            SetWindowLongPtr(window, StyleIndex, new IntPtr(style));
            SetWindowLongPtr(window, OwnerIndex, taskbar);
        }
        SetWindowLongPtr(window, ExStyleIndex, new IntPtr(GetWindowLongPtr(window, ExStyleIndex).ToInt64() | ToolWindow | NoActivate | Layered));
        return child;
    }

    /// The picture of one layered window (<see cref="Attach"/>): its memory DC and DIB section are kept while the client size is unchanged and
    /// let go on a new size and by <see cref="Dispose"/> (usable again after it). A software-drawn child paints into the taskbar's own surface,
    /// which Windows 11's taskbar covers with its XAML content; a layered child is composed above it as itself.
    public sealed class Picture : IDisposable
    {
        private IntPtr _memory, _bitmap, _bits, _old;
        private int _width, _height;

        /// Gives <paramref name="window"/> its picture with per-pixel alpha: <paramref name="fill"/> writes its client area's premultiplied BGRA
        /// pixels (bits, width, height; rows of width * 4 bytes, top row first, all of them: the buffer still holds the last picture). Windows
        /// passes a click through a fully clear pixel, so each one is then given <paramref name="hitAlpha"/> and the whole window takes the mouse.
        public void Paint(IntPtr window, Action<IntPtr, int, int> fill, byte hitAlpha)
        {
            if (!GetClientRect(window, out var client) || client.Right <= 0 || client.Bottom <= 0 || !Fit(client.Right, client.Bottom)) return;
            fill(_bits, _width, _height);
            GiveHitAlpha(hitAlpha);
            var size = new Size { Cx = _width, Cy = _height };
            var screen = GetDC(IntPtr.Zero);
            try
            {
                var origin = default(Point);
                var blend = new Blend { Alpha = 255, Format = SourceAlpha };
                UpdateLayeredWindow(window, screen, IntPtr.Zero, ref size, _memory, ref origin, 0, ref blend, UseAlpha);
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }

        private bool Fit(int width, int height)
        {
            if (_bitmap != IntPtr.Zero && width == _width && height == _height) return true;
            Dispose();
            var header = new BitmapHeader { Size = Marshal.SizeOf<BitmapHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var screen = GetDC(IntPtr.Zero);
            try
            {
                _memory = CreateCompatibleDC(screen);
                _bitmap = CreateDIBSection(screen, ref header, 0, out _bits, IntPtr.Zero, 0);
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
            if (_memory == IntPtr.Zero || _bitmap == IntPtr.Zero)
            {
                Dispose();
                return false;
            }
            (_width, _height) = (width, height);
            _old = SelectObject(_memory, _bitmap);
            return true;
        }

        private unsafe void GiveHitAlpha(byte alpha)
        {
            var pixels = new Span<uint>((void*)_bits, _width * _height);
            var catcher = (uint)alpha << 24;
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i] >> 24 == 0) pixels[i] = catcher;
        }

        public void Dispose()
        {
            if (_memory != IntPtr.Zero && _old != IntPtr.Zero) SelectObject(_memory, _old);
            if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
            if (_memory != IntPtr.Zero) DeleteDC(_memory);
            (_memory, _bitmap, _bits, _old, _width, _height) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0);
        }
    }

    /// True when a full-screen app (Windows' own answer, <see cref="Mouse.FullScreenWatcher.AppInFront"/>) is in front on the monitor of
    /// <paramref name="taskbar"/>: Windows hides the taskbar there, so an owned window over it must hide too.
    public static bool FullScreenOn(IntPtr taskbar) =>
        Mouse.FullScreenWatcher.AppInFront() && MonitorFromWindow(GetForegroundWindow(), DefaultToNearest) == MonitorFromWindow(taskbar, DefaultToNearest);

    /// Shows <paramref name="window"/> again without activating it, or hides it.
    public static void Show(IntPtr window, bool shown) => ShowWindow(window, shown ? ShowNoActivate : Hide);

    /// Moves <paramref name="window"/> to <paramref name="place"/> and to the top of its siblings (or of the topmost windows when owned).
    public static void Put(IntPtr window, TaskbarPlace place)
    {
        var at = new Point { X = place.X, Y = place.Y };
        ScreenToClient(GetAncestor(window, AncestorParent), ref at); // the desktop's client is the screen, so an owned window is unchanged
        SetWindowPos(window, IntPtr.Zero, at.X, at.Y, 0, 0, NoSize | NoActivateOnMove); // after = zero: the top
    }

    /// True when another child of the taskbar is drawn over <paramref name="window"/> (Explorer re-ordered its own parts).
    public static bool IsCovered(IntPtr window) =>
        (GetWindowLongPtr(window, StyleIndex).ToInt64() & Child) != 0 && GetWindow(window, PreviousSibling) != IntPtr.Zero;

    /// The DPI Windows draws <paramref name="window"/> at (the taskbar's: its screen's scale).
    public static int Dpi(IntPtr window) => (int)GetDpiForWindow(window);

    /// True when the taskbar is light (Windows' "system" colour mode), so text on it is dark.
    public static bool IsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    private const int OwnerIndex = -8, StyleIndex = -16, ExStyleIndex = -20;
    private const long Child = 0x40000000, Popup = 0x80000000, ToolWindow = 0x80, NoActivate = 0x08000000, Layered = 0x80000;
    private const uint NoSize = 0x0001, NoActivateOnMove = 0x0010, AncestorParent = 1, PreviousSibling = 3, DefaultToNearest = 2, UseAlpha = 2;
    private const int Hide = 0, ShowNoActivate = 4;
    private const byte SourceAlpha = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blend
    {
        public byte Op, Flags, Alpha, Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, IntPtr at, ref Size size, IntPtr source, ref Point from, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
