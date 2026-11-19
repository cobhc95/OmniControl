namespace OmniControl.App.Features.Input.Mouse;

/// Owns <c>trackpad-settings-original.json</c>: for each Windows touchpad gesture OmniControl has turned off, the raw value it had before
/// (null = the registry value was missing), keyed by <c>WindowsGesture</c> name. <c>GestureTakeover</c> adds an entry before it changes
/// Windows and removes it only once the value is back, so the file always holds what is still to be given back (also after a crash).
internal static class TrackpadBackup
{
    private static readonly DataStore Store = DataStores.TrackpadOriginal;

    public static Dictionary<string, int?>? Load() => AppDataStore.Load<Dictionary<string, int?>>(Store);

    public static void Save(IReadOnlyDictionary<string, int?> originals) => AppDataStore.Save(Store, originals);
}
