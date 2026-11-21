using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// What Setup's "Choose features" page chose (tools/setup.iss writes it beside its receipts, where every user may read it): the ids of the
/// features left unticked, comma separated.
public static class SetupFeatures
{
    private const string Key = @"SOFTWARE\OmniControl", Value = "FeaturesOff";

    /// Setup's choice as it wrote it; null when no Setup wrote one (a portable copy, a Setup from before the page).
    public static string? Off()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            return key?.GetValue(Value) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // not readable: the app keeps its own choice
        }
    }
}
