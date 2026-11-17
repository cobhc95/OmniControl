using System.Runtime.CompilerServices;
using OmniControl.App.Services;

namespace OmniControl.Tests;

/// <summary>Points the app-data folder (settings, errors log) at a temp folder before any test runs, so tests never touch the owner's real %APPDATA%\OmniControl.</summary>
internal static class IsolatedAppData
{
    [ModuleInitializer]
    internal static void Init()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OmniControl-tests", Environment.ProcessId.ToString());
        Directory.CreateDirectory(folder);
        AppDataStore.Folder = folder;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temp leftovers are harmless */ }
        };
    }
}
