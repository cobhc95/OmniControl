using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// <summary>What Windows has for a program in the current user's start-up list.</summary>
public enum StartupState
{
    /// <summary>No entry.</summary>
    Off,

    /// <summary>An entry that Windows will run at sign-in.</summary>
    On,

    /// <summary>An entry exists, but the user turned it off in Task Manager or Settings (StartupApproved), so Windows skips it.</summary>
    DisabledByUser,
}

/// <summary>
/// One value in the current user's Run key (no administrator rights needed) and its StartupApproved flag, which Task Manager
/// and Settings use to switch an entry off without deleting it. The caller owns the name and the command line.
/// </summary>
public static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>StartupApproved values are 12 bytes; the first byte is even (2) when enabled and odd (3) when the user disabled it.</summary>
    private const int ApprovedLength = 12;
    private const byte ApprovedEnabled = 2;

    /// <summary>The command line stored for the entry, or null when there is none (or the key can't be read).</summary>
    public static string? Command(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // unreadable registry key: treat as not registered
        }
    }

    public static StartupState StateOf(string name)
    {
        if (Command(name) is null) return StartupState.Off;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            if (key?.GetValue(name) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1) return StartupState.DisabledByUser;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Flag unreadable: Windows runs an entry without one.
        }
        return StartupState.On;
    }

    /// <summary>
    /// Adds or rewrites the entry, or removes it when the command is null. <paramref name="clearDisabled"/>: also clears a
    /// "disabled by the user" flag (when the user asks for it on); false keeps the user's Task Manager choice (a path repair).
    /// </summary>
    /// <returns>False when the registry refused the change.</returns>
    public static bool Set(string name, string? command, bool clearDisabled = true)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (command is null)
            {
                run.DeleteValue(name, throwOnMissingValue: false);
                return true;
            }
            run.SetValue(name, command);
            if (!clearDisabled) return true;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            if (approved?.GetValue(name) is byte[])
            {
                var enabled = new byte[ApprovedLength];
                enabled[0] = ApprovedEnabled;
                approved.SetValue(name, enabled, RegistryValueKind.Binary);
            }
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
