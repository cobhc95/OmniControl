using OmniControl.App.Features.Volume.Pages.Easy;

namespace OmniControl.Tests;

/// "Restore audio defaults": what changes, what stays, the copy taken first, the undo, and the "already at the defaults" test. Fakes only: no file, no Windows setting.
public class AudioDefaultsTests
{
    private sealed class FakeSnapshot : IDefaultsSnapshot
    {
        public string? Json;
        public int Saves;
        public void Save(MixerSettings settings) { Json = JsonSerializer.Serialize(settings); Saves++; }
        public MixerSettings? Load() => Json is null ? null : JsonSerializer.Deserialize<MixerSettings>(Json);
    }

    /// Changes the settings the way <see cref="MixerService"/> does and records every call; the snapshot count is noted at the first change.
    private sealed class FakeMixer : IAudioDefaultsMixer
    {
        private readonly FakeSnapshot? _snapshot;
        public MixerSettings Settings { get; }
        public List<string> Calls { get; } = new();
        public int SnapshotsAtFirstCall = -1;

        public FakeMixer(MixerSettings settings, FakeSnapshot? snapshot = null) { Settings = settings; _snapshot = snapshot; }

        private void Note(string call)
        {
            if (Calls.Count == 0) SnapshotsAtFirstCall = _snapshot?.Saves ?? -1;
            Calls.Add(call);
        }

        public List<FxSlot> FxChain(string? key) =>
            key is null ? Settings.MasterFx
            : MixerService.IsInput(key) ? Settings.Inputs.First(i => i.Id == MixerService.InputIdOf(key)).Fx
            : Settings.Combined.FirstOrDefault(c => c.Key == key)?.Fx ?? Settings.Apps.First(a => a.Key == key).Fx;
        public void SetAdded(string key, bool added) { Note($"added:{key}"); Settings.Apps.First(a => a.Key == key).Added = added; }
        public void AddCombined(CombinedDevice device) { Note($"addcombo:{device.Key}"); Settings.Combined.Add(device); }
        public void RemoveCombined(string key)
        {
            Note($"removecombo:{key}");
            Settings.Combined.RemoveAll(c => c.Key == key);
            foreach (var app in Settings.Apps) app.PlaysOn.Remove(key);
            Settings.MasterPlaysOn.Remove(key);
        }
        public void SetMarkedCables(IEnumerable<Core.Engine.CableMark> marks)
        {
            var list = marks.ToList();
            if (JsonSerializer.Serialize(list) == JsonSerializer.Serialize(Settings.MarkedCables)) return;
            Note("marks");
            Settings.MarkedCables = list;
        }
        public void SetFxOn(string? key, bool on)
        {
            Note($"fx:{key}:{on}");
            if (key is null) Settings.MasterFxOn = on;
            else if (MixerService.IsInput(key)) Settings.Inputs.First(i => i.Id == MixerService.InputIdOf(key)).FxOn = on;
            else if (Settings.Combined.FirstOrDefault(c => c.Key == key) is { } c) c.FxOn = on;
            else Settings.Apps.First(a => a.Key == key).FxOn = on;
        }
        public void SaveFx(string? key) => Note($"save:{key}");
        public void SetFxRoute(string key, EffectsRoute route) { Note($"route:{key}"); Settings.Apps.First(a => a.Key == key).FxRoute = route.Id; }
        public void SetPlaysOn(string key, IEnumerable<string> targets) { Note($"plays:{key}"); Settings.Apps.First(a => a.Key == key).PlaysOn = targets.ToList(); }
        public void SetMasterPlaysOn(IEnumerable<string> targets)
        {
            var list = targets.ToList();
            if (list.SequenceEqual(Settings.MasterPlaysOn)) return;
            Note("masterplays");
            Settings.MasterPlaysOn = list;
        }
        public void SetMasterMethod(EffectsRoute route)
        {
            if (Settings.MasterMethod == route.Id) return;
            Note("method");
            Settings.MasterMethod = route.Id;
        }
        public void SetMicMethod(string key, EffectsRoute route) { Note($"micmethod:{key}"); Settings.Inputs.First(i => i.Id == MixerService.InputIdOf(key)).Method = route.Id; }
        public void SetCables(IEnumerable<string> forbidden)
        {
            var off = forbidden.ToList();
            if (off.SequenceEqual(Settings.ForbiddenCables)) return;
            Note("cables");
            Settings.ForbiddenCables = off;
        }
        public void Save() { }
    }

    private static FxSlot Boosted()
    {
        var chain = new List<FxSlot>();
        return EasyEq.Write(chain, new[] { 6, 0, 0, 0, 0 });
    }

    /// A busy PC: a bus, effects everywhere, forced methods, routing, cables off, a combined device.
    private static MixerSettings Busy()
    {
        var s = new MixerSettings
        {
            MasterFxOn = true, MasterFx = new() { Boosted() }, MasterMethod = EffectsRules.MasterApo.Id,
            MasterPlaysOn = new() { "dev-a", "dev-b" }, ForbiddenCables = new() { "cable-1", "cable-2" },
        };
        s.Apps.Add(new AppAudioEntry
        {
            Key = "c:\\a.exe", Name = "A", Volume = 40, Pan = 20, Muted = true, Added = true, FxOn = true,
            FxRoute = EffectsRules.RouteCable.Id, PlaysOn = new() { "dev-a" }, Fx = new() { Boosted() },
        });
        s.Apps.Add(new AppAudioEntry { Key = "c:\\b.exe", Name = "B", Volume = 80 });
        s.Inputs.Add(new InputFxEntry { Id = "mic1", Name = "Mic", FxOn = true, Fx = new() { Boosted() } });
        var both = new CombinedDevice { Name = "Both", FxOn = true, Fx = new() { Boosted() } };
        s.Combined.Add(both);
        s.Apps[1].PlaysOn = new() { both.Key };
        s.MasterPlaysOn.Add(both.Key);
        s.MarkedCables.Add(new Core.Engine.CableMark { Name = "Mine", PlaybackId = "p", RecordingId = "r" });
        return s;
    }

    [Fact]
    public void Restore_RemovesEveryChainCombinedDeviceMethodRouteAndMark()
    {
        var snapshot = new FakeSnapshot();
        var mixer = new FakeMixer(Busy(), snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        var s = mixer.Settings;
        Assert.False(s.MasterFxOn);
        Assert.Empty(s.MasterFx);
        Assert.All(s.Apps, a => { Assert.False(a.FxOn); Assert.Empty(a.Fx); Assert.Equal(string.Empty, a.FxRoute); Assert.Empty(a.PlaysOn); });
        Assert.All(s.Inputs, i => { Assert.False(i.FxOn); Assert.Empty(i.Fx); });
        Assert.Empty(s.Combined);
        Assert.Empty(s.MasterPlaysOn);
        Assert.Empty(s.MarkedCables);
        Assert.Equal(string.Empty, s.MasterMethod);
        Assert.Empty(s.ForbiddenCables);
    }

    [Fact]
    public void Restore_RemovesCombinedDevicesThroughTheRemovalPath_SoPlaysOnReferencesAreCleaned()
    {
        var snapshot = new FakeSnapshot();
        var settings = Busy();
        var key = settings.Combined[0].Key;
        var mixer = new FakeMixer(settings, snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        Assert.Contains($"removecombo:{key}", mixer.Calls);
        Assert.DoesNotContain(settings.Apps, a => a.PlaysOn.Contains(key));
        Assert.DoesNotContain(key, settings.MasterPlaysOn);
    }

    [Fact]
    public void Restore_KeepsLevelsMutesAndBalance_ButDropsTheAddedMark()
    {
        var snapshot = new FakeSnapshot();
        var mixer = new FakeMixer(Busy(), snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        var s = mixer.Settings;
        var a = s.Apps[0];
        Assert.Equal((40, 20, true, false), (a.Volume, a.Pan, a.Muted, a.Added));
        Assert.Equal(80, s.Apps[1].Volume);
        Assert.Equal(2, s.Apps.Count);
        Assert.True(a.KeepsRow); // still listed: it holds a non-default level, mute and balance
    }

    [Fact]
    public void Restore_AnAddedProgramWithOnlyDefaultsIsNoLongerListed()
    {
        var settings = new MixerSettings();
        settings.Apps.Add(new AppAudioEntry { Key = "k", Added = true });
        Assert.True(settings.Apps[0].KeepsRow);
        AudioDefaults.Restore(new FakeMixer(settings), new FakeSnapshot());
        Assert.False(settings.Apps[0].KeepsRow);
    }

    [Fact]
    public void Restore_TakesTheCopyBeforeChangingAnything()
    {
        var snapshot = new FakeSnapshot();
        var settings = Busy();
        var before = JsonSerializer.Serialize(settings);
        var mixer = new FakeMixer(settings, snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        Assert.Equal(1, mixer.SnapshotsAtFirstCall);
        Assert.Equal(before, snapshot.Json);
    }

    [Fact]
    public void Undo_PutsBackExactlyWhatWasChanged_AndNothingElse()
    {
        var snapshot = new FakeSnapshot();
        var settings = Busy();
        var original = JsonSerializer.Serialize(settings);
        var mixer = new FakeMixer(settings, snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        settings.Apps[0].Volume = 10; // changed after the restore: the undo must leave it
        Assert.True(AudioDefaults.Undo(mixer, snapshot));
        Assert.Equal(10, settings.Apps[0].Volume);
        settings.Apps[0].Volume = 40;
        Assert.Equal(original, JsonSerializer.Serialize(settings));
    }

    [Fact]
    public void Undo_BringsBackChainsAndCombinedDevicesAndRoutes()
    {
        var snapshot = new FakeSnapshot();
        var settings = Busy();
        var key = settings.Combined[0].Key;
        var mixer = new FakeMixer(settings, snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        Assert.True(AudioDefaults.Undo(mixer, snapshot));
        Assert.Single(settings.MasterFx);
        Assert.Single(settings.Apps[0].Fx);
        Assert.Single(settings.Inputs[0].Fx);
        Assert.Equal(key, Assert.Single(settings.Combined).Key);
        Assert.Single(settings.Combined[0].Fx);
        Assert.Contains(key, settings.Apps[1].PlaysOn);
        Assert.Contains(key, settings.MasterPlaysOn);
        Assert.Single(settings.MarkedCables);
        Assert.True(settings.Apps[0].Added);
    }

    [Fact]
    public void Undo_WithoutACopy_ChangesNothingAndSaysSo()
    {
        var mixer = new FakeMixer(Busy());
        Assert.False(AudioDefaults.Undo(mixer, new FakeSnapshot()));
        Assert.Empty(mixer.Calls);
    }

    [Fact]
    public void AtDefaults_IsFalseForABusyPc_TrueAfterTheRestore_AndForAFreshInstall()
    {
        Assert.False(AudioDefaults.IsAtDefaults(Busy()));
        var snapshot = new FakeSnapshot();
        var mixer = new FakeMixer(Busy(), snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        Assert.True(AudioDefaults.IsAtDefaults(mixer.Settings));
        Assert.True(AudioDefaults.IsAtDefaults(new MixerSettings()));
    }

    [Fact]
    public void AtDefaults_AnyOneDifferenceCounts_AndVolumesDoNot()
    {
        Assert.False(AudioDefaults.IsAtDefaults(new MixerSettings { MasterFxOn = true }));
        Assert.False(AudioDefaults.IsAtDefaults(new MixerSettings { MasterMethod = EffectsRules.MasterBus.Id }));
        Assert.False(AudioDefaults.IsAtDefaults(new MixerSettings { ForbiddenCables = new() { "x" } }));
        Assert.False(AudioDefaults.IsAtDefaults(new MixerSettings { Combined = new() { new CombinedDevice() } }));
        Assert.False(AudioDefaults.IsAtDefaults(new MixerSettings { MasterFx = new() { Boosted() } }));
        var withChain = new MixerSettings();
        withChain.Apps.Add(new AppAudioEntry { Key = "k", Fx = new() { Boosted() } });
        Assert.False(AudioDefaults.IsAtDefaults(withChain));
        var withApp = new MixerSettings();
        withApp.Apps.Add(new AppAudioEntry { Key = "k", Volume = 30, Muted = true, Pan = -10 });
        Assert.True(AudioDefaults.IsAtDefaults(withApp));
        withApp.Apps[0].PlaysOn = new() { "dev" };
        Assert.False(AudioDefaults.IsAtDefaults(withApp));
    }

    [Fact]
    public void Restore_AtDefaults_MakesNoChange()
    {
        var snapshot = new FakeSnapshot();
        var mixer = new FakeMixer(new MixerSettings(), snapshot);
        AudioDefaults.Restore(mixer, snapshot);
        Assert.Empty(mixer.Calls);
    }

    [Fact]
    public void TheQuestion_SaysEverythingIsRemovedAndLevelsStay()
    {
        var question = EasyRestore.Question;
        Assert.Contains("Removes all effects", question.Text);
        Assert.Contains("combined devices", question.Text);
        Assert.Contains("You can undo this.", question.Text);
        Assert.Equal("Restore", question.Button);
    }
}
