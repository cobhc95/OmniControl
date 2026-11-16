using System.Text.Json;
using OmniControl.App.Audio;

namespace OmniControl.Tests;

/// <summary>The effects switch remembers when the user turned it off, so an added effect only switches it on otherwise.</summary>
public class FxUserOffTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void AddingSwitchesOnOnlyWhenOffAndNotUserOff(bool on, bool userOff, bool expected) =>
        Assert.Equal(expected, MixerService.SwitchesOnWhenAdded(on, userOff));

    [Fact]
    public void OldSettingsLoadAsNotUserOff()
    {
        var s = JsonSerializer.Deserialize<MixerSettings>("{\"MasterFxOn\":false,\"Apps\":[{\"Key\":\"a\",\"FxOn\":false}],\"Inputs\":[{\"Id\":\"i\"}]}")!;
        Assert.False(s.MasterFxUserOff);
        Assert.False(s.Apps[0].FxUserOff);
        Assert.False(s.Inputs[0].FxUserOff);
        Assert.False(new CombinedDevice().FxUserOff);
    }

    [Fact]
    public void RestoreDefaultsClearsTheFlag()
    {
        var s = new MixerSettings { MasterFxUserOff = true };
        s.Apps.Add(new AppAudioEntry { Key = "a", FxUserOff = true });
        s.Inputs.Add(new InputFxEntry { Id = "i", FxUserOff = true });
        var target = AudioDefaults.DefaultsOf(s);
        Assert.False(target.MasterFxUserOff);
        Assert.False(target.Apps[0].FxUserOff);
        Assert.False(target.Inputs[0].FxUserOff);
        Assert.False(AudioDefaults.IsAtDefaults(s));
    }
}
