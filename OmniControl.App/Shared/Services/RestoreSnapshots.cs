namespace OmniControl.App.Shared.Services;

/// Where the copy of the settings taken before a "Restore defaults" is kept, so an undo can read it (a copy per section, by its key).
public interface ISnapshotStore
{
    void Save(string key, IReadOnlyDictionary<string, string> copies);

    /// The copies saved under <paramref name="key"/>, or null when there are none or they cannot be read.
    Dictionary<string, string>? Load(string key);
}

/// The copies as <c>&lt;key&gt;-before-restore.json</c> in the app data folder (<see cref="AppDataStore"/>).
public sealed class FileSnapshotStore : ISnapshotStore
{
    private static DataStore FileOf(string key) => DataStores.BeforeRestore.Named(key);

    public void Save(string key, IReadOnlyDictionary<string, string> copies) => AppDataStore.Save(FileOf(key), copies);

    public Dictionary<string, string>? Load(string key) => AppDataStore.Load<Dictionary<string, string>>(FileOf(key));
}
