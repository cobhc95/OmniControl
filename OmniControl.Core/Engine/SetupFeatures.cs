using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// What Setup's "Choose features" page chose (tools/setup.iss writes it beside its receipts, where every user may read it): the ids of the
/// features left unticked, comma separated, and when Setup wrote them, so each Setup run is told apart even when it chose the same.
public static class SetupFeatures
{
    private const string Key = @"SOFTWARE\OmniControl", Value = "FeaturesOff", Written = "FeaturesWritten";

    /// One Setup run's choice: <paramref name="Off"/> as Setup wrote it; <paramref name="Run"/> differs for every run that wrote one.
    public sealed record Choice(string Off, string Run);

    /// Setup's choice; null when no Setup wrote one (a portable copy, a Setup from before the page).
    public static Choice? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            if (key?.GetValue(Value) is not string off) return null;
            // A Setup from before the time was written: the choice alone tells its runs apart.
            return new Choice(off, key.GetValue(Written) is string at ? $"{at}|{off}" : off);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // not readable: the app keeps its own choice
        }
    }
}
