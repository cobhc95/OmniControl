using OmniControl.App.Services;

namespace OmniControl.App.SettingsImport;

/// <summary>One tick box of the import window: the stores that share an area's name in <see cref="DataStores"/>, under their group heading.</summary>
public sealed record ImportArea(string Group, string Name, string? Note, IReadOnlyList<DataStore> Stores);

/// <summary>
/// The areas an import from another PC can take, made from the <see cref="DataStores"/> master list (never typed by hand): every saved store
/// with an <see cref="DataStore.Import"/> place and not <see cref="DataStore.MachineSpecific"/>, grouped by area, in the order of
/// <see cref="ImportGroups.All"/> and then of the master list.
/// </summary>
public static class ImportAreas
{
    public static IReadOnlyList<ImportArea> All { get; } = Build();

    public static ImportArea? Find(string name) => All.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    private static List<ImportArea> Build()
    {
        var offered = DataStores.BackedUp.Where(s => s.Import is not null && !s.MachineSpecific).ToList();
        return ImportGroups.All
            .SelectMany(group => offered.Where(s => s.Import!.Group == group).GroupBy(s => s.Import!.Area))
            .Select(g => new ImportArea(g.First().Import!.Group, g.Key, g.First().Import!.Note, g.ToList()))
            .ToList();
    }
}
