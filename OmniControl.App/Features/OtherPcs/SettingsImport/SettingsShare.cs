using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// What this PC hands another paired PC that asks to import its settings: which areas it has and the files of one area. Only the stores of
/// <see cref="ImportAreas"/> are ever read, so a machine-specific store never leaves; a store's <see cref="DataStore.NotImported"/> properties
/// (the approvals of commands) are removed first, and passwords and pairing keys are not in any store at all (they live in the Windows
/// Credential Manager). It reads the saved files, as an export does.
/// <param name="redactDevices">Takes the text of the smart devices file out of everything private (one rule with the device set-ups
/// handed out); null when it cannot be read, and the file is then left out.</param>
public sealed class SettingsShare(Func<string, string?> redactDevices)
{
    /// The areas with something saved, with their counts.
    public IReadOnlyList<AreaOffer> Offer() => ImportAreas.Offers(ImportAreas.Shared, AppDataStore.LoadText);

    /// The saved files of the area by file name, or null for a name that is not an area. A damaged file is left out.
    public Dictionary<string, string>? Pack(string areaName)
    {
        if (ImportAreas.Find(ImportAreas.Shared, areaName) is not { } area) return null;
        var files = new Dictionary<string, string>();
        foreach (var store in area.Stores)
        {
            if (AppDataStore.LoadText(store) is not { } text || !BackupFile.TryParse(text, out var node)) continue;
            var packed = JsonMerge.KeepOwn(node, null, store.NotImported ?? Array.Empty<string>())?.ToJsonString() ?? text;
            if (store != DataStores.Devices) files[store.FileName] = packed;
            else if (redactDevices(packed) is { } redacted) files[store.FileName] = redacted;
        }
        return files;
    }
}
