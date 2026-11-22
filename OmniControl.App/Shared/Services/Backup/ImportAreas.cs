namespace OmniControl.App.Shared.Services.Backup;

/// One tick box of the settings window: the stores that share an area's name in <see cref="DataStores"/>, under their group heading.
public sealed record ImportArea(string Group, string Name, string? Note, IReadOnlyList<DataStore> Stores);

/// What a source holds of one area: its name and, for lists, how many entries (null for plain settings).
public sealed record AreaOffer(string Area, int? Count);

/// The areas of the settings window (export, import from a file, import from another PC), made from the <see cref="DataStores"/> master
/// list (never typed by hand): every saved store by its <see cref="DataStore.Import"/> place, grouped by area, in the order of
/// <see cref="ImportGroups.All"/> and then of the master list.
public static class ImportAreas
{
    /// Every area a backup file holds.
    public static IReadOnlyList<ImportArea> All { get; } = Build(DataStores.BackedUp);

    /// The areas another PC may read: the <see cref="DataStore.MachineSpecific"/> stores never leave this PC.
    public static IReadOnlyList<ImportArea> Shared { get; } = Build(DataStores.BackedUp.Where(s => !s.MachineSpecific));

    public static ImportArea? Find(IReadOnlyList<ImportArea> areas, string name) => areas.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    /// The areas with something in <paramref name="textOf"/> (a store's json, or null), each with its count.
    public static IReadOnlyList<AreaOffer> Offers(IReadOnlyList<ImportArea> areas, Func<DataStore, string?> textOf)
    {
        var offers = new List<AreaOffer>();
        foreach (var area in areas)
        {
            var saved = area.Stores.Select(s => (Store: s, Text: textOf(s))).Where(t => t.Text is not null).ToList();
            if (saved.Count == 0) continue;
            var counts = saved.Select(t => BackupFile.CountOfText(t.Store, t.Text!)).OfType<int>().ToList();
            offers.Add(new AreaOffer(area.Name, counts.Count > 0 ? counts.Sum() : null));
        }
        return offers;
    }

    private static List<ImportArea> Build(IEnumerable<DataStore> stores)
    {
        var offered = stores.Where(s => s.Import is not null).ToList();
        return ImportGroups.All
            .SelectMany(group => offered.Where(s => s.Import!.Group == group).GroupBy(s => s.Import!.Area))
            .Select(g => new ImportArea(g.First().Import!.Group, g.Key, g.First().Import!.Note, g.ToList()))
            .ToList();
    }
}
