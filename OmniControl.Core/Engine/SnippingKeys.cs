using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// <summary>What the two Windows values held before OmniControl changed them (null = the value did not exist), so turning the switch off puts them back as they were.</summary>
public sealed record SnippingKeysBackup(int? PrintScreen, string? DisabledHotkeys);

/// <summary>
/// Windows' own screenshot keys, in the current user's registry (no administrator rights needed): Print Screen opening Snipping Tool
/// (Control Panel\Keyboard, takes effect at once) and Win+Shift+S (Explorer's DisabledHotkeys list, which Explorer reads when it starts,
/// so it applies after Explorer restarts or the next sign-in). Every call returns false when the registry could not be read or written.
/// </summary>
public static class SnippingKeys
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string PrintScreenValue = "PrintScreenKeyForSnippingEnabled";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DisabledValue = "DisabledHotkeys";
    private const string ShiftS = "S";

    /// <summary>The values as they are now; null when they can't be read.</summary>
    public static SnippingKeysBackup? Capture()
    {
        try
        {
            using var keyboard = Registry.CurrentUser.OpenSubKey(KeyboardKey);
            using var advanced = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return new SnippingKeysBackup(keyboard?.GetValue(PrintScreenValue) as int?, advanced?.GetValue(DisabledValue) as string);
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return null; // unreadable key: nothing to remember, nothing to change
        }
    }

    /// <summary>True when Explorer (which reads Win+Shift+S's setting when it starts) was started after <paramref name="changedUtc"/>; unknown reads as true so no tag is left hanging.</summary>
    public static bool ExplorerStartedSince(DateTime changedUtc)
    {
        try
        {
            var started = System.Diagnostics.Process.GetProcessesByName("explorer").Select(p => p.StartTime.ToUniversalTime()).DefaultIfEmpty(DateTime.MaxValue).Min();
            return started >= changedUtc;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return true; // start time not readable (access denied or exited): say nothing rather than a wrong tag
        }
    }

    /// <summary>True when Print Screen no longer opens Snipping Tool and Win+Shift+S is released.</summary>
    public static bool IsOff(SnippingKeysBackup? now) =>
        now is { PrintScreen: 0, DisabledHotkeys: { } keys } && keys.Contains(ShiftS, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns both Windows keys off.</summary>
    public static bool TurnOff()
    {
        try
        {
            using var keyboard = Registry.CurrentUser.CreateSubKey(KeyboardKey);
            keyboard.SetValue(PrintScreenValue, 0, RegistryValueKind.DWord);
            using var advanced = Registry.CurrentUser.CreateSubKey(AdvancedKey);
            var keys = advanced.GetValue(DisabledValue) as string ?? string.Empty;
            if (!keys.Contains(ShiftS, StringComparison.OrdinalIgnoreCase)) advanced.SetValue(DisabledValue, keys + ShiftS, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Puts the values back as <paramref name="before"/> had them: the Print Screen value is set or removed as it was; of the hotkey letters only
    /// the S OmniControl added is taken out again, so a letter the user added meanwhile stays.
    /// </summary>
    public static bool Restore(SnippingKeysBackup before)
    {
        try
        {
            using var keyboard = Registry.CurrentUser.CreateSubKey(KeyboardKey);
            if (before.PrintScreen is { } value) keyboard.SetValue(PrintScreenValue, value, RegistryValueKind.DWord);
            else keyboard.DeleteValue(PrintScreenValue, throwOnMissingValue: false);
            if (before.DisabledHotkeys?.Contains(ShiftS, StringComparison.OrdinalIgnoreCase) == true) return true; // S was the user's own
            using var advanced = Registry.CurrentUser.CreateSubKey(AdvancedKey);
            var keys = (advanced.GetValue(DisabledValue) as string ?? string.Empty).Replace(ShiftS, string.Empty, StringComparison.OrdinalIgnoreCase);
            if (keys.Length == 0 && before.DisabledHotkeys is null) advanced.DeleteValue(DisabledValue, throwOnMissingValue: false);
            else advanced.SetValue(DisabledValue, keys, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            return false;
        }
    }

    private static bool IsRegistryFailure(Exception ex) => ex is System.Security.SecurityException or UnauthorizedAccessException or IOException;
}
