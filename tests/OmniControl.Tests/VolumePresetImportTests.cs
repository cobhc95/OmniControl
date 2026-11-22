using System.Text.Json.Nodes;
using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.Tests;

public sealed class VolumePresetImportTests
{
    [Fact]
    public void An_imported_volume_preset_loses_its_plugin_file_and_saved_state()
    {
        var file = new VolumePresetFile();
        var setup = new MixerSettings();
        setup.MasterFx.Add(new FxSlot { Kind = "vst", Name = "Plug", Path = @"C:\evil\plug.dll", State = "c2VjcmV0" });
        file.Presets.Add(new VolumePreset { Name = "P", Id = "1", Setup = setup });

        var node = JsonNode.Parse(JsonSerializer.Serialize(file))!;
        JsonMerge.StripInside(node, DataStores.VolumePresets.NotImportedInside!);
        var text = node.ToJsonString();

        Assert.DoesNotContain("evil", text);
        Assert.DoesNotContain("c2VjcmV0", text);
        Assert.Contains("Plug", text);
        Assert.Equal(DataStores.Mixer.NotImportedInside, DataStores.VolumePresets.NotImportedInside);
    }
}

