using System.Text.Json;
using OmniControl.App.Features.Resources;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Tests;

/// The network adapter chosen for Download and Upload on their own: kept per reading, and Auto when it is gone.
public class SpeedAdapterTests
{
    [Fact]
    public void Choices_AreKeptPerReading_AndSurviveSaving()
    {
        var setup = new ResourcesSetup();
        setup.SetSpeedAdapter(Reading.DownloadId, "{A}");
        setup.SetSpeedAdapter(Reading.UploadId, "{B}");
        var back = JsonSerializer.Deserialize<ResourcesSetup>(JsonSerializer.Serialize(setup))!;
        Assert.Equal("{A}", back.SpeedAdapterOf(Reading.DownloadId));
        Assert.Equal("{B}", back.SpeedAdapterOf(Reading.UploadId));
        Assert.True(setup.Same(back));
    }

    [Fact]
    public void Auto_IsNoEntry()
    {
        var setup = new ResourcesSetup();
        Assert.Null(setup.SpeedAdapterOf(Reading.UploadId));
        setup.SetSpeedAdapter(Reading.UploadId, "{B}");
        setup.SetSpeedAdapter(Reading.UploadId, null);
        Assert.Empty(setup.SpeedAdapters);
    }

    [Fact]
    public void OnlyTheSpeedReadings_HaveTheChoice()
    {
        Assert.True(Reading.HasAdapterChoice(Reading.UploadId));
        Assert.True(Reading.HasAdapterChoice(Reading.DownloadId));
        Assert.False(Reading.HasAdapterChoice(Reading.GpuId));
    }

    [Fact]
    public void AnAdapterThatIsGone_ReadsAsAuto()
    {
        using var sampler = new ResourceSampler();
        var gone = new SpeedAdapters("{no-such-adapter}", "{no-such-adapter}");
        sampler.Read(SampleParts.Network, null, null, gone);
        var sample = sampler.Read(SampleParts.Network, null, null, gone);
        Assert.Null(sample.DownloadFrom);
        Assert.Null(sample.UploadFrom);
    }

    [Fact]
    public void AnAdapterThatIsThere_IsReadOnItsOwn()
    {
        using var sampler = new ResourceSampler();
        var up = sampler.Read(SampleParts.Network | SampleParts.Adapters, null).Adapters.FirstOrDefault(a => a.IsUp);
        if (up is null) return; // no connected adapter on this machine
        var own = new SpeedAdapters(up.Id, up.Id);
        sampler.Read(SampleParts.Network, null, null, own);
        var sample = sampler.Read(SampleParts.Network, null, null, own);
        Assert.Equal(up.Name, sample.DownloadFrom);
        Assert.Equal(up.Name, sample.UploadFrom);
    }
}
