namespace OmniControl.App.Services;

/// <summary>Where OmniControl keeps its files by default; kept apart from <see cref="AppDataStore"/> so a developer tool can name it without opening the store.</summary>
public static class AppDataLocation
{
    public static string Default => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl");
}
