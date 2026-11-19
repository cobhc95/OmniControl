using OmniControl.Core.Engine.Trackpad;

namespace OmniControl.Tests;

/// Turning Windows' own touchpad gestures off and giving them back, against a fake settings store (nothing on the PC is touched).
public class GestureTakeoverTests
{
    /// Windows' settings in memory: registry values (missing = absent key) and the parameter bits, with a write log, plus the shell's own
    /// copy of the registry-route values, which (as on the reference PC) follows the stored value only when it is told to (Announce).
    private sealed class FakeStore : ITouchpadSettingsStore
    {
        public readonly Dictionary<WindowsGesture, int?> Values = new();
        public readonly Dictionary<WindowsGesture, int?> Shell = new();
        public readonly List<(WindowsGesture Gesture, int? Value)> Writes = new();
        public readonly List<WindowsGesture> Announced = new();
        public string? Refuse;
        public bool IgnoreWrites; // the write is accepted but Windows keeps its own value
        public bool ShellIgnores; // the shell takes the call but keeps its old copy

        public FakeStore()
        {
            // As on the reference PC: the swipe and three-finger tap values missing (Windows' defaults), taps and zoom on.
            foreach (var s in WindowsGestureSettings.All) Values[s.Gesture] = Shell[s.Gesture] = s.Route == SettingRoute.Registry ? null : 1;
        }

        public string? Read(WindowsGestureSetting setting, out int? value)
        {
            value = Values[setting.Gesture];
            return null;
        }

        public string? Write(WindowsGestureSetting setting, int? value)
        {
            if (Refuse is not null) return Refuse;
            Writes.Add((setting.Gesture, value));
            if (IgnoreWrites) return null;
            Values[setting.Gesture] = value;
            if (setting.Route != SettingRoute.Registry) Shell[setting.Gesture] = value; // Windows' input applies these as written
            return null;
        }

        public string? Announce(WindowsGestureSetting setting)
        {
            Announced.Add(setting.Gesture);
            if (!ShellIgnores) Shell[setting.Gesture] = Values[setting.Gesture];
            return null;
        }

        public string? ReadInEffect(WindowsGestureSetting setting, out int? value)
        {
            value = Shell[setting.Gesture];
            return null;
        }
    }

    private sealed class FakeBackup
    {
        public Dictionary<string, int?>? Saved;
        public int SaveCount;
        public void Save(IReadOnlyDictionary<string, int?> originals)
        {
            Saved = originals.ToDictionary(p => p.Key, p => p.Value);
            SaveCount++;
        }
    }

    private static HashSet<WindowsGesture> Set(params WindowsGesture[] g) => g.ToHashSet();

    [Fact]
    public void TurnsOffOnlyTheWantedGesture_AndKeepsTheOriginalFirst()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        Assert.True(takeover.Update(Set(WindowsGesture.ThreeFingerTap)));

        Assert.Equal(0, store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Single(store.Writes);
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
        Assert.Equal(TakeoverState.None, takeover.StatusOf(WindowsGesture.ThreeFingerSwipes).State);
        Assert.True(backup.Saved!.ContainsKey(nameof(WindowsGesture.ThreeFingerTap)));
        Assert.Null(backup.Saved[nameof(WindowsGesture.ThreeFingerTap)]); // the value was missing: giving it back deletes it again
    }

    [Fact]
    public void GestureAlreadyOffInWindows_IsLeftAlone()
    {
        var store = new FakeStore();
        store.Values[WindowsGesture.ThreeFingerSwipes] = 0;
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        takeover.Update(Set(WindowsGesture.ThreeFingerSwipes));

        Assert.Empty(store.Writes);
        Assert.Null(backup.Saved);
        Assert.Equal(TakeoverState.None, takeover.StatusOf(WindowsGesture.ThreeFingerSwipes).State);
    }

    [Fact]
    public void NoLongerWanted_GivesTheOriginalBack()
    {
        var store = new FakeStore();
        store.Values[WindowsGesture.ThreeFingerTap] = 3; // play/pause
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        takeover.Update(Set(WindowsGesture.ThreeFingerTap, WindowsGesture.PinchToZoom));
        takeover.Update(Set());

        Assert.Equal(3, store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Equal(1, store.Values[WindowsGesture.PinchToZoom]);
        Assert.Empty(backup.Saved!);
        Assert.False(takeover.AnyTurnedOff);
    }

    [Fact]
    public void RestoreAll_GivesEverythingBack_AndDeletesAMissingValueAgain()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);
        takeover.Update(WindowsGestureSettings.All.Select(s => s.Gesture).ToHashSet());
        Assert.All(WindowsGestureSettings.All, s => Assert.Equal(s.Off, store.Values[s.Gesture]));

        takeover.RestoreAll();

        Assert.All(WindowsGestureSettings.All, s => Assert.True(s.IsOn(store.Values[s.Gesture])));
        Assert.Null(store.Values[WindowsGesture.ThreeFingerSwipes]);
        Assert.Empty(backup.Saved!);
    }

    [Fact]
    public void CrashedRun_IsRestoredAtNextStart_WhenNoLongerWanted()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        new GestureTakeover(store, null, backup.Save).Update(Set(WindowsGesture.ThreeFingerTap)); // the run that crashed: no give-back

        var next = new GestureTakeover(store, backup.Saved, backup.Save);
        Assert.Equal(TakeoverState.TurnedOff, next.StatusOf(WindowsGesture.ThreeFingerTap).State);
        next.Update(Set()); // the switch is off, or the action was removed

        Assert.Null(store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Empty(backup.Saved!);
    }

    [Fact]
    public void CrashedRun_StillWanted_KeepsTheFirstOriginal()
    {
        var store = new FakeStore();
        store.Values[WindowsGesture.ThreeFingerTap] = 4; // middle button
        var backup = new FakeBackup();
        new GestureTakeover(store, null, backup.Save).Update(Set(WindowsGesture.ThreeFingerTap));

        var next = new GestureTakeover(store, backup.Saved, backup.Save);
        next.Update(Set(WindowsGesture.ThreeFingerTap));

        Assert.Equal(4, backup.Saved![nameof(WindowsGesture.ThreeFingerTap)]); // never replaced by OmniControl's own 0
        next.RestoreAll();
        Assert.Equal(4, store.Values[WindowsGesture.ThreeFingerTap]);
    }

    [Fact]
    public void RewrittenExternally_IsTurnedOffAgainEveryTime_NeverGivenUp()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var takeover = new GestureTakeover(store, null, backup.Save, () => now);
        takeover.Update(Set(WindowsGesture.ThreeFingerTap));

        // Far more often than the old three-a-minute limit: the Settings app, a driver or Windows writing it back (a registry change event each time).
        for (var i = 0; i < 20; i++)
        {
            now = now.AddMilliseconds(OmniControl.Core.CoreTiming.TrackpadTakeoverReapplyGapMs);
            store.Values[WindowsGesture.ThreeFingerTap] = 1;
            takeover.Check();
            Assert.Equal(0, store.Values[WindowsGesture.ThreeFingerTap]);
            Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
        }
        Assert.Null(backup.Saved![nameof(WindowsGesture.ThreeFingerTap)]); // the first original is still kept for the give-back
    }

    [Fact]
    public void RewrittenTwiceWithinTheGap_IsTurnedOffAgainAtTheNextLook()
    {
        var store = new FakeStore();
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save, () => now);
        takeover.Update(Set(WindowsGesture.ThreeFingerSwipes));
        store.Values[WindowsGesture.ThreeFingerSwipes] = 1;
        takeover.Check();

        store.Values[WindowsGesture.ThreeFingerSwipes] = 1; // a program writing it straight back: no tight loop
        takeover.Check();
        Assert.Equal(1, store.Values[WindowsGesture.ThreeFingerSwipes]);

        now = now.AddMilliseconds(OmniControl.Core.CoreTiming.TrackpadTakeoverReapplyGapMs); // the regular look
        takeover.Check();
        Assert.Equal(0, store.Values[WindowsGesture.ThreeFingerSwipes]);
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerSwipes).State);
    }

    [Fact]
    public void ResumeOrTouchpadReconnect_ReapplyThroughUpdate_AndLogTheValueReadBack()
    {
        var store = new FakeStore();
        var log = new List<string>();
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save) { Log = log.Add };
        takeover.Update(Set(WindowsGesture.TwoFingerTap));
        Assert.Contains(log, l => l.Contains("TwoFingerTapEnabled") && l.Contains("reads back 0"));

        store.Values[WindowsGesture.TwoFingerTap] = 1; // Windows put its default back while asleep, or for the touchpad that arrived
        log.Clear();
        Assert.False(takeover.Update(Set(WindowsGesture.TwoFingerTap))); // a touchpad arrival (Refresh) applies the wanted set again

        Assert.Equal(0, store.Values[WindowsGesture.TwoFingerTap]);
        Assert.Contains(log, l => l.Contains("back on (1)") && l.Contains("reads back 0"));
    }

    [Fact]
    public void WriteWindowsDidNotKeep_IsAFailure_WithItsReason()
    {
        var store = new FakeStore { IgnoreWrites = true }; // Windows (a policy or a driver) silently keeps its own value
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        takeover.Update(Set(WindowsGesture.PinchToZoom));

        var status = takeover.StatusOf(WindowsGesture.PinchToZoom);
        Assert.Equal(TakeoverState.Failed, status.State);
        Assert.NotNull(status.Problem);
        Assert.Empty(backup.Saved!); // nothing changed, nothing to give back
    }

    [Fact]
    public void Check_WithNothingChanged_WritesNothing()
    {
        var store = new FakeStore();
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save);
        takeover.Update(Set(WindowsGesture.PinchToZoom));
        store.Writes.Clear();

        Assert.False(takeover.Check());
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void UserChangedTheValueMeanwhile_TheirChoiceStays()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);
        takeover.Update(Set(WindowsGesture.ThreeFingerTap));
        store.Values[WindowsGesture.ThreeFingerTap] = 3; // the user picked play/pause in Settings
        store.Writes.Clear();

        takeover.Update(Set());

        Assert.Empty(store.Writes);
        Assert.Equal(3, store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Empty(backup.Saved!);
    }

    [Fact]
    public void RefusedWrite_IsReported_AndLeavesNoBackupEntry()
    {
        var store = new FakeStore { Refuse = "Windows didn't let OmniControl change three-finger tap." };
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        takeover.Update(Set(WindowsGesture.ThreeFingerTap));

        var status = takeover.StatusOf(WindowsGesture.ThreeFingerTap);
        Assert.Equal(TakeoverState.Failed, status.State);
        Assert.Equal(store.Refuse, status.Problem);
        Assert.Empty(backup.Saved!);
        Assert.Null(store.Values[WindowsGesture.ThreeFingerTap]);
    }

    [Fact]
    public void RefusedGiveBack_KeepsTheBackup()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);
        takeover.Update(Set(WindowsGesture.TwoFingerTap));
        store.Refuse = "Refused.";

        takeover.RestoreAll();

        Assert.True(backup.Saved!.ContainsKey(nameof(WindowsGesture.TwoFingerTap)));
        Assert.NotNull(takeover.StatusOf(WindowsGesture.TwoFingerTap).Problem);
        store.Refuse = null;
        takeover.RestoreAll();
        Assert.Equal(1, store.Values[WindowsGesture.TwoFingerTap]);
        Assert.Empty(backup.Saved!);
    }

    [Fact]
    public void NothingWanted_NeverWrites()
    {
        var store = new FakeStore();
        var backup = new FakeBackup();
        var takeover = new GestureTakeover(store, null, backup.Save);

        takeover.Update(Set());
        takeover.Check();
        takeover.RestoreAll();

        Assert.Empty(store.Writes);
        Assert.Equal(0, backup.SaveCount);
    }

    [Fact]
    public void FollowingTheWantedSet_TakesOverGivesBackAndTakesOverAgain()
    {
        var store = new FakeStore();
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save);

        takeover.Update(Set(WindowsGesture.ThreeFingerTap));
        Assert.Equal(0, store.Values[WindowsGesture.ThreeFingerTap]);
        takeover.Update(Set()); // the action's "also let Windows do its own gesture" was ticked
        Assert.Null(store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Equal(TakeoverState.None, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
        takeover.Update(Set(WindowsGesture.ThreeFingerTap)); // and unticked again
        Assert.Equal(0, store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
    }

    [Fact]
    public void TurningOff_TellsWindowsToUseIt_AndReadsBackWhatWindowsActsOn()
    {
        var store = new FakeStore();
        var log = new List<string>();
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save) { Log = log.Add };

        takeover.Update(Set(WindowsGesture.ThreeFingerTap));

        Assert.Contains(WindowsGesture.ThreeFingerTap, store.Announced); // the step the registry write alone was missing
        Assert.Equal(0, store.Shell[WindowsGesture.ThreeFingerTap]);
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
        Assert.Contains(log, l => l.Contains("reads back 0, Windows acts on 0"));
    }

    [Fact]
    public void AStoredOffTheShellNeverTook_IsAnnouncedAtTheNextStart_WithoutAWrite()
    {
        // The reference PC: the previous run stored 0, but the shell still acted on its own copy (Open search).
        var store = new FakeStore();
        store.Values[WindowsGesture.ThreeFingerTap] = 0;
        var saved = new Dictionary<string, int?> { [nameof(WindowsGesture.ThreeFingerTap)] = null };
        var takeover = new GestureTakeover(store, saved, new FakeBackup().Save);

        takeover.Update(Set(WindowsGesture.ThreeFingerTap));

        Assert.Empty(store.Writes);
        Assert.Contains(WindowsGesture.ThreeFingerTap, store.Announced);
        Assert.Equal(0, store.Shell[WindowsGesture.ThreeFingerTap]);
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
    }

    [Fact]
    public void ShellKeepingItsOldCopy_IsAFailure_KeepsTheOriginal_AndIsToldAgainUntilItTakes()
    {
        var store = new FakeStore { ShellIgnores = true };
        var backup = new FakeBackup();
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var takeover = new GestureTakeover(store, null, backup.Save, () => now);

        takeover.Update(Set(WindowsGesture.ThreeFingerTap));

        var status = takeover.StatusOf(WindowsGesture.ThreeFingerTap);
        Assert.Equal(TakeoverState.Failed, status.State);
        Assert.Contains("still acts", status.Problem);
        Assert.True(backup.Saved!.ContainsKey(nameof(WindowsGesture.ThreeFingerTap))); // stored 0, so it is still to be given back

        var announced = store.Announced.Count;
        now = now.AddMilliseconds(OmniControl.Core.CoreTiming.TrackpadTakeoverReapplyGapMs);
        takeover.Check();
        Assert.True(store.Announced.Count > announced); // told again by the regular look

        store.ShellIgnores = false;
        now = now.AddMilliseconds(OmniControl.Core.CoreTiming.TrackpadTakeoverReapplyGapMs);
        Assert.True(takeover.Check());
        Assert.Equal(TakeoverState.TurnedOff, takeover.StatusOf(WindowsGesture.ThreeFingerTap).State);
        Assert.Equal(0, store.Shell[WindowsGesture.ThreeFingerTap]);

        takeover.RestoreAll();
        Assert.Null(store.Values[WindowsGesture.ThreeFingerTap]);
        Assert.Null(store.Shell[WindowsGesture.ThreeFingerTap]); // Windows' default (Open search) again, told too
    }

    [Fact]
    public void AnUpdate_TellsTheShellAgain_WhenItStillActsOnItsOldCopy()
    {
        var store = new FakeStore();
        var takeover = new GestureTakeover(store, null, new FakeBackup().Save);
        takeover.Update(Set(WindowsGesture.ThreeFingerSwipes));
        store.Shell[WindowsGesture.ThreeFingerSwipes] = 1; // Explorer restarted with a copy it read before (a sign-in race, say)
        store.Writes.Clear();

        takeover.Update(Set(WindowsGesture.ThreeFingerSwipes)); // the page opened, a mapping changed, a touchpad arrived

        Assert.Empty(store.Writes);
        Assert.Equal(0, store.Shell[WindowsGesture.ThreeFingerSwipes]);
    }

    [Fact]
    public void EveryRegistrySetting_HasAShellId_AndNoOtherDoes() =>
        Assert.All(WindowsGestureSettings.All, s => Assert.Equal(s.Route == SettingRoute.Registry, s.ShellId >= 0));

    [Fact]
    public void EveryGesture_HasExactlyOneWindowsSetting() =>
        Assert.All(TrackpadGestures.All, g => Assert.Equal(g.AlsoWindows, WindowsGestureSettings.Of(g.AlsoWindows).Gesture));
}
