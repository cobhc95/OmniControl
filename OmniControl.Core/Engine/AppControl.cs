using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine;

/// <summary>Recognises a refusal by Windows App Control (Smart App Control, App Control for Business, AppLocker), for every caller.</summary>
internal static partial class AppControl
{
    /// <summary>ERROR_ACCESS_DISABLED_BY_POLICY (Software Restriction Policies, AppLocker).</summary>
    private const int DisabledByPolicy = 1260;

    /// <summary>
    /// ERROR_SYSTEM_INTEGRITY_* (winerror.h, 4550 onward): App Control for Business and Smart App Control refusals; 4551 is
    /// "An Application Control policy has blocked this file" (seen as 0x800711C7).
    /// </summary>
    private const int SystemIntegrityFirst = 4550, SystemIntegrityLast = 4580;

    /// <summary>True when a Windows error code means a policy refused the file.</summary>
    public static bool IsBlock(int win32Error) =>
        win32Error == DisabledByPolicy || win32Error is >= SystemIntegrityFirst and <= SystemIntegrityLast;

    /// <summary>
    /// True when a failed DLL load was a policy refusal. .NET gives the Windows error only inside the (translated) message, as
    /// the HRESULT "0x8007xxxx", which is the same in every language.
    /// </summary>
    public static bool IsBlock(Exception ex) =>
        Win32InMessage().Match(ex.Message) is { Success: true } m && IsBlock(Convert.ToInt32(m.Groups[1].Value, 16));

    [GeneratedRegex("0x8007([0-9A-Fa-f]{4})")]
    private static partial Regex Win32InMessage();
}
