using System.Runtime.InteropServices;
using static OmniControl.Core.Engine.Trackpad.RawInputNative;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>
/// One precision touchpad's report layout (from its HID preparsed data) and the reading of its reports into frames of contacts.
/// A frame may arrive over several reports ("hybrid" touchpads put the contact count in the first and 0 in the rest), so contacts
/// are gathered until the count is reached. Reading allocates nothing. Contact ids are not read: the recogniser only needs which
/// fingers touch and where, and the average and spread do not depend on their order.
/// </summary>
internal sealed unsafe class TouchpadDevice : IDisposable
{
    /// <summary>Most contacts kept per frame (Windows' precision touchpad spec asks for 3 to 5).</summary>
    private const int MaxContacts = 10;

    /// <summary>Size of the buffer for the pressed usages of one finger collection (tip switch, confidence...).</summary>
    private const int UsageBufferLength = 16;

    /// <summary>One finger collection of the report: its link collection and the logical ranges of its X and Y.</summary>
    private readonly record struct Finger(ushort Link, int XMin, int YMin);

    private IntPtr _preparsed;
    private readonly Finger[] _fingers;
    private readonly bool _hasCount;
    private readonly ushort _countLink;
    private readonly float _longerSide; // the larger logical span: both axes are divided by it, so a unit is the same distance across and down
    private readonly TouchContact[] _frame = new TouchContact[MaxContacts];
    private int _expected, _collected;

    private TouchpadDevice(IntPtr preparsed, Finger[] fingers, bool hasCount, ushort countLink, float longerSide)
    {
        _preparsed = preparsed;
        _fingers = fingers;
        _hasCount = hasCount;
        _countLink = countLink;
        _longerSide = longerSide;
    }

    /// <summary>Every precision touchpad Windows lists now (raw input handles).</summary>
    public static List<IntPtr> ListHandles()
    {
        var found = new List<IntPtr>();
        uint count = 0, itemSize = (uint)Marshal.SizeOf<RawInputDeviceListItem>();
        if (GetRawInputDeviceList(null, ref count, itemSize) == uint.MaxValue || count == 0) return found;
        var items = new RawInputDeviceListItem[count];
        var listed = GetRawInputDeviceList(items, ref count, itemSize);
        if (listed == uint.MaxValue) return found;
        for (var i = 0; i < listed; i++)
            if (items[i].Type == RimTypeHid && IsTouchpad(items[i].Device)) found.Add(items[i].Device);
        return found;
    }

    private static bool IsTouchpad(IntPtr device)
    {
        var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
        var size = info.Size;
        return GetRawInputDeviceInfo(device, RidiDeviceInfo, &info, ref size) != uint.MaxValue
            && info.UsagePage == DigitizerPage && info.Usage == TouchpadUsage;
    }

    /// <summary>Reads the touchpad's layout; null when it is not a touchpad or its layout has no finger positions.</summary>
    public static TouchpadDevice? Open(IntPtr device)
    {
        if (!IsTouchpad(device)) return null;
        uint size = 0;
        if (GetRawInputDeviceInfo(device, RidiPreparsedData, null, ref size) != 0 || size == 0) return null;
        var preparsed = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputDeviceInfo(device, RidiPreparsedData, (void*)preparsed, ref size) == uint.MaxValue) return null;
            if (Layout(preparsed) is not { } layout) return null;
            var opened = new TouchpadDevice(preparsed, layout.Fingers, layout.HasCount, layout.CountLink, layout.LongerSide);
            preparsed = IntPtr.Zero; // owned by the device now
            return opened;
        }
        finally
        {
            if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        }
    }

    private static (Finger[] Fingers, bool HasCount, ushort CountLink, float LongerSide)? Layout(IntPtr preparsed)
    {
        if (HidP_GetCaps(preparsed, out var caps) != HidpStatusSuccess || caps.NumberInputValueCaps == 0) return null;
        var length = caps.NumberInputValueCaps;
        var values = new HidpValueCaps[length];
        if (HidP_GetValueCaps(HidpInput, values, ref length, preparsed) != HidpStatusSuccess) return null;

        bool hasCount = false;
        ushort countLink = 0;
        var xs = new Dictionary<ushort, (int Min, int Span)>();
        var ys = new Dictionary<ushort, (int Min, int Span)>();
        foreach (var v in values.AsSpan(0, length))
        {
            if (v.IsRange != 0) continue;
            if (v.UsagePage == DigitizerPage && v.Usage == ContactCountUsage) (hasCount, countLink) = (true, v.LinkCollection);
            else if (v.UsagePage == GenericDesktopPage && v.Usage == XUsage) xs[v.LinkCollection] = RangeOf(v);
            else if (v.UsagePage == GenericDesktopPage && v.Usage == YUsage) ys[v.LinkCollection] = RangeOf(v);
        }
        var fingers = xs.Keys.Where(ys.ContainsKey).OrderBy(link => link)
            .Select(link => new Finger(link, xs[link].Min, ys[link].Min)).ToArray();
        if (fingers.Length == 0) return null;
        var longer = Math.Max(xs.Values.Max(r => r.Span), ys.Values.Max(r => r.Span));
        return longer <= 0 ? null : (fingers, hasCount, countLink, longer);
    }

    /// <summary>A value's logical range; a range that reads backwards (a 16-bit maximum read as signed) is taken as the field's full unsigned range.</summary>
    private static (int Min, int Span) RangeOf(HidpValueCaps v) =>
        v.LogicalMax > v.LogicalMin ? (v.LogicalMin, v.LogicalMax - v.LogicalMin)
        : (0, v.BitSize is > 0 and < 31 ? (1 << v.BitSize) - 1 : int.MaxValue);

    /// <summary>Reads one HID report. True when it completes a frame, which is then in <paramref name="frame"/> (valid until the next call).</summary>
    public bool Read(byte* report, uint length, out ReadOnlySpan<TouchContact> frame)
    {
        frame = default;
        if (_preparsed == IntPtr.Zero) return false;
        if (!_hasCount) (_expected, _collected) = (Math.Min(_fingers.Length, MaxContacts), 0);
        else if (Value(DigitizerPage, _countLink, ContactCountUsage, report, length, out var count) && count > 0)
            (_expected, _collected) = ((int)Math.Min(count, (uint)MaxContacts), 0);
        if (_expected == 0) return false;

        var usages = stackalloc ushort[UsageBufferLength];
        foreach (var finger in _fingers)
        {
            if (_collected >= _expected) break;
            if (!Value(GenericDesktopPage, finger.Link, XUsage, report, length, out var x)
                || !Value(GenericDesktopPage, finger.Link, YUsage, report, length, out var y)) continue;
            _frame[_collected++] = new TouchContact
            {
                X = ((int)x - finger.XMin) / _longerSide,
                Y = ((int)y - finger.YMin) / _longerSide,
                Touching = Touching(finger.Link, report, length, usages),
            };
        }
        if (_collected < _expected) return false;
        frame = _frame.AsSpan(0, _collected);
        _expected = 0;
        return true;
    }

    private bool Value(ushort page, ushort link, ushort usage, byte* report, uint length, out uint value) =>
        HidP_GetUsageValue(HidpInput, page, link, usage, out value, _preparsed, report, length) == HidpStatusSuccess;

    /// <summary>The finger's tip switch. A collection without one (rare) counts as touching; the touch then ends by the gap in reports.</summary>
    private bool Touching(ushort link, byte* report, uint length, ushort* usages)
    {
        uint count = UsageBufferLength;
        if (HidP_GetUsages(HidpInput, DigitizerPage, link, usages, ref count, _preparsed, report, length) != HidpStatusSuccess) return true;
        for (var i = 0; i < count; i++)
            if (usages[i] == TipSwitchUsage) return true;
        return false;
    }

    public void Dispose()
    {
        if (_preparsed == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_preparsed);
        _preparsed = IntPtr.Zero;
    }
}
