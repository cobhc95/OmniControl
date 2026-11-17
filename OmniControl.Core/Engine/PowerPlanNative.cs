using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>The powrprof calls of <see cref="PowerPlanService"/>; every call returns a Win32 error code (0 = success) and nothing else in the program calls them.</summary>
internal static class PowerPlanNative
{
    public const uint AccessScheme = 16, AccessSubgroup = 17, AccessIndividualSetting = 18;
    public const uint NoMoreItems = 259, MoreData = 234;
    private const uint AttributeHide = 1;

    [DllImport("powrprof.dll")] public static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint flags, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr userRoot, out IntPtr scheme);
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr userRoot, in Guid scheme);
    [DllImport("powrprof.dll")] public static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadDCValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerWriteACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerWriteDCValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadACDefaultIndex(IntPtr root, in Guid personality, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadDCDefaultIndex(IntPtr root, in Guid personality, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadValueMin(IntPtr root, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadValueMax(IntPtr root, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadValueIncrement(IntPtr root, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] public static extern uint PowerReadValueUnitsSpecifier(IntPtr root, in Guid subgroup, in Guid setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] public static extern uint PowerReadPossibleFriendlyName(IntPtr root, in Guid subgroup, in Guid setting, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] public static extern uint PowerReadPossibleValue(IntPtr root, in Guid subgroup, in Guid setting, out uint type, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] public static extern uint PowerReadSettingAttributes(in Guid subgroup, in Guid setting);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);

    /// <summary>Windows' own "hide this setting" mark (the control panel dialog leaves such settings out).</summary>
    public static bool IsHidden(in Guid subgroup, in Guid setting) => (PowerReadSettingAttributes(subgroup, setting) & AttributeHide) != 0;

    /// <summary>The GUID at index <paramref name="index"/> of an enumeration; null when there is no more (or it failed).</summary>
    public static Guid? EnumerateGuid(IntPtr scheme, IntPtr subgroup, uint flags, uint index)
    {
        var buffer = new byte[16];
        uint size = 16;
        return PowerEnumerate(IntPtr.Zero, scheme, subgroup, flags, index, buffer, ref size) == 0 ? new Guid(buffer) : null;
    }

    /// <summary>The scheme that is active now.</summary>
    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    /// <summary>Reads a UTF-16 text with the usual ask-for-the-size-then-read pair; null when there is none.</summary>
    public static string? ReadText(ReadBytes read)
    {
        uint size = 0;
        var code = read(null, ref size);
        if (code != 0 && code != MoreData || size < 2) return null;
        var buffer = new byte[size];
        if (read(buffer, ref size) != 0) return null;
        var text = Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        if (text.Length == 0) return null;
        if (text[0] != '@') return text;
        var resolved = new StringBuilder(256); // an indirect string ("@resource.dll,-123")
        return SHLoadIndirectString(text, resolved, (uint)resolved.Capacity, IntPtr.Zero) == 0 ? resolved.ToString() : text;
    }

    public delegate uint ReadBytes(byte[]? buffer, ref uint size);
}
