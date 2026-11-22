using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Traffic;
using OmniControl.App.Shared.Services.Backup;
using OmniControl.App.Shared.Ui.Pages;

namespace OmniControl.Tests;

/// Network Traffic Control's settings have their own store (traffic.json): an older resources.json is split once at start and in every
/// backup, and Resources' and Network Traffic Control's presets never touch each other's settings.
[Collection(nameof(TrafficSplitTests))]
public sealed class TrafficSplitTests : IDisposable
{
    private const string OldResources = """
        {"Setup":{"ReadEverySeconds":3,"Apps":[{"Id":"game","DownloadOn":true,"DownloadKbps":64}],"Scoped":[{"Adapter":"wifi","Apps":[{"Id":"game","Blocked":true}]}],"Pinned":["game"]},
         "PacketFilterOn":false,"Presets":[{"Id":"p1","Name":"Work","Setup":{"ReadEverySeconds":5,"Apps":[{"Id":"old"}],"Scoped":[],"Pinned":["old"]}}],
         "TrafficPresets":[{"Id":"t1","Name":"Night","Apps":[{"Id":"game","Blocked":true}]}],"TrafficRules":["traffic.use.wifi"]}
        """;

    private readonly string _work = Directory.CreateTempSubdirectory("omni-traffic").FullName;

    public TrafficSplitTests() => Clear();

    public void Dispose()
    {
        Clear();
        Directory.Delete(_work, recursive: true);
    }

    private static void Clear()
    {
        AppDataStore.FlushSaves();
        AppDataStore.Delete(DataStores.Resources);
        AppDataStore.Delete(DataStores.Traffic);
    }

    [Fact]
    public void The_start_moves_the_live_rules_strips_the_resources_presets_and_runs_once()
    {
        AppDataStore.SaveText(DataStores.Resources, OldResources);

        StoreMoves.RunAtStart();

        var traffic = TrafficSettings.Load();
        Assert.Equal(64, Assert.Single(traffic.Apps).DownloadKbps);
        Assert.True(Assert.Single(Assert.Single(traffic.Scoped).Apps).Blocked);
        Assert.Equal("game", Assert.Single(traffic.Pinned));
        Assert.False(traffic.PacketFilterOn);
        Assert.Equal("Night", Assert.Single(traffic.Presets).Name);
        Assert.Equal("traffic.use.wifi", Assert.Single(traffic.Rules));
        var resources = AppDataStore.LoadText(DataStores.Resources)!;
        foreach (var gone in new[] { "\"Apps\"", "\"Scoped\"", "\"Pinned\"", "TrafficPresets", "TrafficRules", "PacketFilterOn" }) Assert.DoesNotContain(gone, resources);
        Assert.Equal(5, Assert.Single(ResourcesSettings.Load().Presets).Setup.ReadEverySeconds);

        var (before, trafficBefore) = (resources, AppDataStore.LoadText(DataStores.Traffic));
        StoreMoves.RunAtStart();
        Assert.Equal(before, AppDataStore.LoadText(DataStores.Resources));
        Assert.Equal(trafficBefore, AppDataStore.LoadText(DataStores.Traffic));
    }

    [Fact]
    public void The_start_never_replaces_a_traffic_store_already_there()
    {
        AppDataStore.SaveText(DataStores.Traffic, """{"Apps":[{"Id":"mine"}]}""");
        AppDataStore.SaveText(DataStores.Resources, OldResources);

        StoreMoves.RunAtStart();

        Assert.Equal("mine", Assert.Single(TrafficSettings.Load().Apps).Id);
        Assert.DoesNotContain("TrafficPresets", AppDataStore.LoadText(DataStores.Resources)!);
    }

    [Fact]
    public void An_old_backup_offers_and_restores_the_rules_in_their_own_area()
    {
        var old = Directory.CreateDirectory(Path.Combine(_work, "old")).FullName;
        File.WriteAllText(Path.Combine(old, DataStores.Resources.FileName), OldResources);
        var file = Path.Combine(_work, "old." + BackupFile.Extension);
        BackupFile.Write(old, file, "0.3.1");

        var contents = BackupFile.Read(file).Contents!;

        var offered = ImportAreas.Offers(ImportAreas.All, s => contents.Files.GetValueOrDefault(s.FileName)).Select(o => o.Area).ToList();
        Assert.Contains(PageTitles.Traffic, offered);
        Assert.Contains(PageTitles.Resources, offered);
        var traffic = JsonSerializer.Deserialize<TrafficSettings>(contents.Files[DataStores.Traffic.FileName])!;
        Assert.Equal("game", Assert.Single(traffic.Apps).Id);
        Assert.Equal("Night", Assert.Single(traffic.Presets).Name);
        Assert.DoesNotContain("TrafficPresets", contents.Files[DataStores.Resources.FileName]);
    }

    [Fact]
    public void An_older_pcs_resources_file_keeps_its_rules_out_of_resources()
    {
        var upgraded = StoreMoves.Upgrade(new Dictionary<string, string> { [DataStores.Resources.FileName] = OldResources });

        Assert.DoesNotContain("\"Apps\"", upgraded[DataStores.Resources.FileName]);
        Assert.Contains("\"Apps\"", upgraded[DataStores.Traffic.FileName]);
        Assert.Same(upgraded, StoreMoves.Upgrade(upgraded)); // nothing left to move
    }

    [Fact]
    public void Applying_one_page_preset_never_changes_the_other_pages_settings()
    {
        var resources = new ResourcesSettings();
        var traffic = new TrafficSettings { Apps = { new AppNetRule { Id = "game", Blocked = true } } };
        var resourcePresets = new ResourcePresets(resources);
        var trafficPresets = new TrafficPresets(traffic);
        var trafficSaved = trafficPresets.Add().Id!;
        resources.Setup.ReadEverySeconds = 9;
        var resourcesSaved = resourcePresets.Add().Id!;

        var trafficText = JsonSerializer.Serialize(traffic);
        resources.Setup.ReadEverySeconds = 1;
        Assert.Null(resourcePresets.Apply(resourcesSaved));
        Assert.Equal(9, resources.Setup.ReadEverySeconds);
        Assert.Equal(trafficText, JsonSerializer.Serialize(traffic));

        var resourcesText = JsonSerializer.Serialize(resources);
        traffic.Apps.Clear();
        Assert.Null(trafficPresets.Apply(trafficSaved));
        Assert.True(Assert.Single(traffic.Apps).Blocked);
        Assert.Equal(resourcesText, JsonSerializer.Serialize(resources));
    }
}
