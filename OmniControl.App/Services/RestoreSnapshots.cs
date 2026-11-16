namespace OmniControl.App.Services;

/// <summary>Where the copy of the settings taken before a "Restore defaults" is kept, so an undo can read it (a copy per section, by its key).</summary>
public interface ISnapshotStore
{
    void Save(string key, IReadOnlyDictionary<string, string> copies);

    /// <summary>The copies saved under <paramref name="key"/>, or null when there are none or they cannot be read.</summary>
    Dictionary<string, string>? Load(string key);
}

/// <summary>The copies as <c>&lt;key&gt;-before-restore.json</c> in the app data folder (<see cref="AppDataStore"/>).</summary>
public sealed class FileSnapshotStore : ISnapshotStore
{
    private static DataStore FileOf(string key) => DataStores.BeforeRestore.Named(key);

    public void Save(string key, IReadOnlyDictionary<string, string> copies) => AppDataStore.Save(FileOf(key), copies);

    public Dictionary<string, string>? Load(string key) => AppDataStore.Load<Dictionary<string, string>>(FileOf(key));
}
