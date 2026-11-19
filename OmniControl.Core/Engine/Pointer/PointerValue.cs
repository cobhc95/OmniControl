namespace OmniControl.Core.Engine.Pointer;

/// One of Windows' mouse settings that <see cref="PointerSettings"/> reads and writes. The names are stored in the backup of the
/// user's original values, so they never change. The order is the order a full restore writes them in: a value that another one
/// depends on comes first (the trail length before trails on/off, the lines per notch before "one screen at a time").
public enum PointerValue
{
    Speed,
    Precision,
    Swap,
    DoubleClickTime,
    ScrollLines,
    ScrollScreen,
    ScrollChars,
    TrailLength,
    Trails,
    HideWhileTyping,
    SnapToDefault,
    Sonar,
    ClickLock,
}

/// The values Windows accepts for a setting and the value Windows starts with. A switch is 0 (off) or 1 (on).
public sealed record PointerValueInfo(int Min, int Max, int Default)
{
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
}

/// Windows' ranges and defaults for every <see cref="PointerValue"/>, defined once (from the SystemParametersInfo documentation and the Mouse control panel).
public static class PointerValues
{
    private const int Off = 0, On = 1;
    private static readonly PointerValueInfo Switch = new(Off, On, Off);

    private static readonly IReadOnlyDictionary<PointerValue, PointerValueInfo> Table = new Dictionary<PointerValue, PointerValueInfo>
    {
        [PointerValue.Speed] = new(1, 20, 10),
        [PointerValue.Precision] = Switch with { Default = On },
        [PointerValue.Swap] = Switch,
        // The control panel's slider runs 200-900 ms; SetDoubleClickTime would accept up to 5000.
        [PointerValue.DoubleClickTime] = new(200, 900, 500),
        [PointerValue.ScrollLines] = new(1, 100, 3),
        [PointerValue.ScrollScreen] = Switch,
        [PointerValue.ScrollChars] = new(1, 100, 3),
        // The control panel's "short" to "long"; Windows draws n pointers for any n above 1.
        [PointerValue.TrailLength] = new(2, 7, 7),
        [PointerValue.Trails] = Switch,
        // The documentation says off, but Windows 10 and 11 are installed with it on.
        [PointerValue.HideWhileTyping] = Switch with { Default = On },
        [PointerValue.SnapToDefault] = Switch,
        [PointerValue.Sonar] = Switch,
        [PointerValue.ClickLock] = Switch,
    };

    public static PointerValueInfo Info(PointerValue value) => Table[value];

    /// Every value, in restore order.
    public static IReadOnlyList<PointerValue> All { get; } = Enum.GetValues<PointerValue>();
}
