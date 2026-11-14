using OmniControl.App.Services;

namespace OmniControl.App.Mouse;

/// <summary>
/// Owns <c>trackpad-settings-original.json</c>: for each Windows touchpad gesture OmniControl has turned off, the raw value it had before
/// (null = the registry value was missing), keyed by <c>WindowsGesture</c> name. <c>GestureTakeover</c> adds an entry before it changes
/// Windows and removes it only once the value is back, so the file always holds what is still to be given back (also after a crash).
/// </summary>
internal static class TrackpadBackup
{
    private const string FileName = "trackpad-settings-original.json";

    public static Dictionary<string, int?>? Load() => AppDataStore.Load<Dictionary<string, int?>>(FileName);

    public static void Save(IReadOnlyDictionary<string, int?> originals) => AppDataStore.Save(FileName, originals);
}
