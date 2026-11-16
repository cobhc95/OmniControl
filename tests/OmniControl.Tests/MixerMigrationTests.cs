using System.Text.Json;
using OmniControl.App.Audio;

namespace OmniControl.Tests;

/// <summary>Old mixer.json files: a stored sound mode of an older version is ignored, and the per-row effects modes of the oldest ones are still converted.</summary>
public class MixerMigrationTests
{
    [Fact]
    public void AFileWithAStoredSoundMode_LoadsAndNeedsNoMigration()
    {
        var s = JsonSerializer.Deserialize<MixerSettings>("""{"SoundMode":"fast","MasterFxOn":true}""")!;
        Assert.True(s.MasterFxOn);
        Assert.False(MixerMigration.Run(s));
        Assert.True(s.MasterFxOn);
    }

    [Fact]
    public void ARowWhoseOldEffectsModeWasOff_HasItsEffectsSwitchedOff_OnceOnly()
    {
        var s = new MixerSettings { MasterFxOn = true, MasterFxMode = "off" };
        s.Apps.Add(new AppAudioEntry { Key = "a", FxOn = true, FxMode = "capture", DeviceId = "dev", DeviceName = "Speakers" });
        s.Apps.Add(new AppAudioEntry { Key = "b", FxOn = true });

        Assert.True(MixerMigration.Run(s));
        Assert.False(s.MasterFxOn);
        Assert.Null(s.MasterFxMode);
        Assert.True(s.Apps[0].FxOn);
        Assert.Equal(new[] { "dev" }, s.Apps[0].PlaysOn);
        Assert.False(s.Apps[1].FxOn);

        s.Apps[1].FxOn = true;
        Assert.False(MixerMigration.Run(s)); // the old fields are gone: a second run changes nothing
        Assert.True(s.Apps[1].FxOn);
    }
}
