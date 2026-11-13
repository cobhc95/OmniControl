namespace OmniControl.Core.Engine.Pointer;

/// <summary>
/// Reads and writes Windows' mouse settings (<see cref="PointerValue"/>) with SystemParametersInfo, as Windows' own Mouse settings do:
/// a saved write goes to the user's profile and is announced to every window, so it lasts after a restart. Reads are cheap; a write
/// broadcasts a settings change to every window, so callers write off the UI thread. Two pairs share one Windows value: the trail
/// length is the number of pointers while trails are on, and "one screen at a time" is a special number of lines; the last real
/// length and line count seen are kept here so switching back gives them again.
/// </summary>
public sealed class PointerSettings
{
    /// <summary>Windows' own mouse settings, which also hold "Scroll inactive windows when I hover over them" (not offered here: see HANDOVER).</summary>
    public const string WindowsMousePage = "ms-settings:mousetouchpad";

    /// <summary>Windows' pointer size and colour settings (Accessibility), which have no documented way to set them from a program.</summary>
    public const string WindowsPointerLookPage = "ms-settings:easeofaccess-mousepointer";

    private const int Off = 0, On = 1;

    // Windows' own pair of thresholds when acceleration is turned on through the control panel.
    private const int Threshold1 = 6, Threshold2 = 10;

    private readonly object _gate = new();
    private int _lastTrailLength = PointerValues.Info(PointerValue.TrailLength).Default;
    private int _lastLines = PointerValues.Info(PointerValue.ScrollLines).Default;

    /// <summary>The value now in force, or null when Windows won't say.</summary>
    public int? Read(PointerValue value)
    {
        lock (_gate)
        {
            switch (value)
            {
                case PointerValue.Speed: return GetInt(SpiNative.GetMouseSpeed);
                case PointerValue.Precision: return ReadMouse() is { } mouse ? Flag(mouse[2] != 0) : null;
                case PointerValue.Swap: return Flag(SpiNative.GetSystemMetrics(SpiNative.SmSwapButton) != 0);
                case PointerValue.DoubleClickTime: return (int)SpiNative.GetDoubleClickTime();
                case PointerValue.ScrollChars: return GetInt(SpiNative.GetWheelScrollChars);
                case PointerValue.HideWhileTyping: return GetInt(SpiNative.GetMouseVanish) is { } v ? Flag(v != 0) : null;
                case PointerValue.SnapToDefault: return GetInt(SpiNative.GetSnapToDefButton) is { } s ? Flag(s != 0) : null;
                case PointerValue.Sonar: return GetInt(SpiNative.GetMouseSonar) is { } so ? Flag(so != 0) : null;
                case PointerValue.ClickLock: return GetInt(SpiNative.GetMouseClickLock) is { } c ? Flag(c != 0) : null;

                case PointerValue.ScrollLines:
                case PointerValue.ScrollScreen:
                    if (GetInt(SpiNative.GetWheelScrollLines) is not { } lines) return null;
                    var page = (uint)lines == SpiNative.WheelPageScroll;
                    if (!page) _lastLines = lines;
                    return value == PointerValue.ScrollScreen ? Flag(page) : _lastLines;

                case PointerValue.Trails:
                case PointerValue.TrailLength:
                    if (GetInt(SpiNative.GetMouseTrails) is not { } trails) return null;
                    var on = trails > 1; // 0 and 1 both mean off
                    if (on) _lastTrailLength = PointerValues.Info(PointerValue.TrailLength).Clamp(trails);
                    return value == PointerValue.Trails ? Flag(on) : _lastTrailLength;

                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }
    }

    /// <summary>
    /// Sets a value (clamped to <see cref="PointerValues"/>). <paramref name="persist"/> false changes this session only (it ends at
    /// sign-out); used while a button swap waits to be kept. False when Windows refused.
    /// </summary>
    public bool Write(PointerValue value, int wanted, bool persist = true)
    {
        var v = PointerValues.Info(value).Clamp(wanted);
        var flags = persist ? SpiNative.SaveAndBroadcast : SpiNative.BroadcastOnly;
        lock (_gate)
        {
            switch (value)
            {
                case PointerValue.Speed: return SpiNative.Set(SpiNative.SetMouseSpeed, 0, (IntPtr)v, flags);
                case PointerValue.Precision: return WriteMouse(v == On, flags);
                case PointerValue.Swap: return SpiNative.Set(SpiNative.SetMouseButtonSwap, (uint)v, IntPtr.Zero, flags);
                case PointerValue.DoubleClickTime: return SpiNative.Set(SpiNative.SetDoubleClickTime, (uint)v, IntPtr.Zero, flags);
                case PointerValue.ScrollChars: return SpiNative.Set(SpiNative.SetWheelScrollChars, (uint)v, IntPtr.Zero, flags);
                case PointerValue.HideWhileTyping: return SpiNative.Set(SpiNative.SetMouseVanish, 0, (IntPtr)v, flags);
                case PointerValue.SnapToDefault: return SpiNative.Set(SpiNative.SetSnapToDefButton, (uint)v, IntPtr.Zero, flags);
                case PointerValue.Sonar: return SpiNative.Set(SpiNative.SetMouseSonar, 0, (IntPtr)v, flags);
                case PointerValue.ClickLock: return SpiNative.Set(SpiNative.SetMouseClickLock, 0, (IntPtr)v, flags);

                case PointerValue.ScrollLines:
                    _lastLines = v;
                    return SpiNative.Set(SpiNative.SetWheelScrollLines, (uint)v, IntPtr.Zero, flags);
                case PointerValue.ScrollScreen:
                    return SpiNative.Set(SpiNative.SetWheelScrollLines, v == On ? SpiNative.WheelPageScroll : (uint)_lastLines, IntPtr.Zero, flags);

                case PointerValue.TrailLength:
                    _lastTrailLength = v;
                    // While trails are off the length is only kept for when they are turned on.
                    return GetInt(SpiNative.GetMouseTrails) is not > 1 || SpiNative.Set(SpiNative.SetMouseTrails, (uint)v, IntPtr.Zero, flags);
                case PointerValue.Trails:
                    return SpiNative.Set(SpiNative.SetMouseTrails, v == On ? (uint)_lastTrailLength : Off, IntPtr.Zero, flags);

                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }
    }

    private static int Flag(bool on) => on ? On : Off;

    private static int? GetInt(uint action) => SpiNative.GetInt(action, 0, out var value, 0) ? value : null;

    /// <summary>The two acceleration thresholds and the acceleration flag.</summary>
    private static int[]? ReadMouse()
    {
        var mouse = new int[3];
        return SpiNative.Array(SpiNative.GetMouse, 0, mouse, 0) ? mouse : null;
    }

    /// <summary>"Enhance pointer precision": on keeps the user's thresholds (Windows' 6 and 10 when there are none); off is 0, 0, 0 as the control panel writes.</summary>
    private static bool WriteMouse(bool on, uint flags)
    {
        var now = ReadMouse();
        var mouse = on
            ? now is { } m && (m[0] != 0 || m[1] != 0) ? new[] { m[0], m[1], On } : new[] { Threshold1, Threshold2, On }
            : new[] { Off, Off, Off };
        return SpiNative.Array(SpiNative.SetMouse, 0, mouse, flags);
    }
}
