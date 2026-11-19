namespace OmniControl.App.Shared.Services;

/// Where OmniControl keeps its files by default; kept apart from <see cref="AppDataStore"/> so a developer tool can name it without opening the store.
public static class AppDataLocation
{
    public static string Default => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl");
}
