using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.Tests;

/// The settings window's areas: every saved store is in one, an export of some areas holds only those, and an import of some areas
/// changes only theirs.
public sealed class BackupPartsTests : IDisposable
{
    private readonly string _here = Directory.CreateTempSubdirectory("omni-parts").FullName;
    private readonly string _work = Directory.CreateTempSubdirectory("omni-parts-work").FullName;

    public void Dispose()
    {
        Directory.Delete(_here, recursive: true);
        Directory.Delete(_work, recursive: true);
    }

    private void Put(DataStore store, string json) => File.WriteAllText(Path.Combine(_here, store.FileName), json);

    private static ImportArea AreaOf(DataStore store) => ImportAreas.All.Single(a => a.Stores.Contains(store));

    [Fact]
    public void Every_saved_store_is_in_exactly_one_area_and_machine_specific_ones_never_go_to_another_pc()
    {
        foreach (var store in DataStores.BackedUp) Assert.Single(ImportAreas.All, a => a.Stores.Contains(store));
        Assert.DoesNotContain(ImportAreas.Shared.SelectMany(a => a.Stores), s => s.MachineSpecific);
        Assert.Equal(ImportAreas.All.Count, ImportAreas.All.Select(a => a.Name).Distinct().Count());
    }

    [Fact]
    public void An_export_of_some_areas_holds_only_them_and_reads_back_like_any_backup()
    {
        Put(DataStores.Osd, """{"Size":3}""");
        Put(DataStores.Displays, """{"Presets":[]}""");
        Put(DataStores.Automation, """{"Scripts":[{"Id":"a"}]}""");
        var file = Path.Combine(_work, "parts." + BackupFile.Extension);

        BackupFile.Write(_here, file, "9.9", new[] { AreaOf(DataStores.Osd), AreaOf(DataStores.Automation) }.SelectMany(a => a.Stores));
        var contents = BackupFile.Read(file).Contents!;

        Assert.Equal(new[] { DataStores.Automation.FileName, DataStores.Osd.FileName }.Order(), contents.Files.Keys.Order());
        var offered = ImportAreas.Offers(ImportAreas.All, s => contents.Files.GetValueOrDefault(s.FileName)).Select(o => o.Area);
        Assert.Equal(new[] { AreaOf(DataStores.Automation).Name, AreaOf(DataStores.Osd).Name }.Order(), offered.Order());
    }

    [Fact]
    public void An_old_full_backup_lists_every_area_it_holds()
    {
        Put(DataStores.Osd, """{"Size":3}""");
        Put(DataStores.OtherPcs, """{"Pairs":[]}""");
        var file = Path.Combine(_work, "full." + BackupFile.Extension);
        BackupFile.Write(_here, file, "0.3.1"); // no areas given: every saved store, as before

        var contents = BackupFile.Read(file).Contents!;
        var offered = ImportAreas.Offers(ImportAreas.All, s => contents.Files.GetValueOrDefault(s.FileName)).Select(o => o.Area).ToList();

        Assert.Contains(AreaOf(DataStores.Osd).Name, offered);
        Assert.Contains(AreaOf(DataStores.OtherPcs).Name, offered);
    }

    [Fact]
    public void An_import_of_one_area_changes_only_its_stores()
    {
        var osd = DataStores.Osd;
        var other = DataStores.Gestures;
        AppDataStore.SaveText(osd, """{"Size":1}""");
        AppDataStore.SaveText(other, """{"Mine":true}""");
        var hooks = new ImportHooks();
        hooks.OnReload(osd, () => true);
        hooks.OnReload(other, () => true);
        var files = new Dictionary<string, string> { [osd.FileName] = """{"Size":9}""", [other.FileName] = """{"Theirs":true}""" };

        var result = new SettingsImporter(hooks).Apply(AreaOf(osd), files);

        Assert.Equal(AreaOutcome.Checked, result.Outcome);
        Assert.Contains("9", AppDataStore.LoadText(osd));
        Assert.Equal("""{"Mine":true}""", AppDataStore.LoadText(other));
    }
}
