using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// What this PC hands another paired PC that asks to import its settings: which areas it has and the files of one area. Only the stores of
/// <see cref="ImportAreas"/> are ever read, so a machine-specific store never leaves; a store's <see cref="DataStore.NotImported"/> properties
/// (the approvals of commands) are removed first, and passwords and pairing keys are not in any store at all (they live in the Windows
/// Credential Manager). It reads the saved files, as an export does.
public sealed class SettingsShare
{
    /// The areas with something saved, with their counts.
    public IReadOnlyList<AreaOffer> Offer()
    {
        var offers = new List<AreaOffer>();
        foreach (var area in ImportAreas.All)
        {
            var saved = area.Stores.Select(s => (Store: s, Text: AppDataStore.LoadText(s))).Where(t => t.Text is not null).ToList();
            if (saved.Count == 0) continue;
            var counts = saved.Select(t => BackupFile.CountOfText(t.Store, t.Text!)).Where(n => n is not null).Select(n => n!.Value).ToList();
            offers.Add(new AreaOffer(area.Name, counts.Count > 0 ? counts.Sum() : null));
        }
        return offers;
    }

    /// The saved files of the area by file name, or null for a name that is not an area. A damaged file is left out.
    public Dictionary<string, string>? Pack(string areaName)
    {
        if (ImportAreas.Find(areaName) is not { } area) return null;
        var files = new Dictionary<string, string>();
        foreach (var store in area.Stores)
        {
            if (AppDataStore.LoadText(store) is not { } text || !BackupFile.TryParse(text, out var node)) continue;
            files[store.FileName] = JsonMerge.KeepOwn(node, null, store.NotImported ?? Array.Empty<string>())?.ToJsonString() ?? text;
        }
        return files;
    }
}
