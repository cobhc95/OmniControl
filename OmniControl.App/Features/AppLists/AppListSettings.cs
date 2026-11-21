namespace OmniControl.App.Features.AppLists;

/// The app lists, stored as JSON in %APPDATA%\OmniControl\app-lists.json. A new install has none.
public sealed class AppListSettings
{
    public List<AppList> Lists { get; set; } = new();

    private static readonly DataStore Store = DataStores.AppLists;

    public static AppListSettings Load() => AppDataStore.Load<AppListSettings>(Store) ?? new();

    public void Save() => AppDataStore.SaveSoon(Store, this);
}
