using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.Tests;

/// A restore from a backup is all or nothing: stopped by a crash or a failed write at any step, the folder ends up wholly as before or
/// wholly restored after the next start, never a mix. Each step is interrupted in turn through the restore's step hook.
public sealed class RestoreRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("omni-restore").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// Stands for the process ending: nothing in the restore catches it.
    private sealed class Crash : Exception;

    /// Throws at the step numbered <paramref name="at"/> (from 0).
    private sealed class Fault(int at, Func<Exception> make)
    {
        private int _steps;

        public bool Hit { get; private set; }

        public void Step()
        {
            if (_steps++ != at) return;
            Hit = true;
            throw make();
        }
    }

    [Theory]
    [InlineData(RestoreMode.Replace)]
    [InlineData(RestoreMode.Merge)]
    [InlineData(RestoreMode.Wipe)]
    public void A_restore_crashed_at_any_step_is_finished_at_the_next_start(RestoreMode mode)
    {
        var restored = Snapshot(Restored(Staged("whole", mode)));
        var mixed = false;
        for (var at = 0; ; at++)
        {
            var here = Staged($"crash-{at}", mode);
            var before = Snapshot(here);
            var fault = new Fault(at, () => new Crash());
            try { PendingRestore.ApplyPending(here, fault.Step); }
            catch (Crash) { }
            if (!fault.Hit)
            {
                Assert.Equal(restored, Snapshot(here));
                break;
            }
            var stopped = Snapshot(here);
            mixed |= !stopped.SequenceEqual(before) && !stopped.SequenceEqual(restored);

            Assert.NotNull(PendingRestore.ApplyPending(here)); // the next start
            Assert.Equal(restored, Snapshot(here));
            Assert.False(Directory.Exists(Path.Combine(here, "pending-restore")));
            Assert.Null(PendingRestore.ApplyPending(here)); // carried out once only
        }
        Assert.True(mixed); // some crash did stop it with the stores half changed
    }

    [Theory]
    [InlineData(RestoreMode.Replace)]
    [InlineData(RestoreMode.Merge)]
    [InlineData(RestoreMode.Wipe)]
    public void A_restore_whose_write_fails_at_any_step_leaves_all_old_or_all_new(RestoreMode mode)
    {
        var restored = Snapshot(Restored(Staged("whole", mode)));
        for (var at = 0; ; at++)
        {
            var here = Staged($"fail-{at}", mode);
            var before = Snapshot(here);
            var fault = new Fault(at, () => new IOException("disk said no"));
            Assert.NotNull(PendingRestore.ApplyPending(here, fault.Step));
            if (!fault.Hit) break;

            PendingRestore.ApplyPending(here); // the next start
            var after = Snapshot(here);
            Assert.True(after.SequenceEqual(before) || after.SequenceEqual(restored), $"mixed after a failure at step {at}");
            Assert.Null(PendingRestore.ApplyPending(here));
        }
    }

    [Fact]
    public void A_restore_whose_write_and_rollback_both_fail_keeps_its_journal_until_the_next_start_recovers()
    {
        var restored = Snapshot(Restored(Staged("whole", RestoreMode.Replace)));
        var kept = false;
        for (var at = 0; ; at++)
        {
            var here = Staged($"stuck-{at}", RestoreMode.Replace);
            var before = Snapshot(here);
            var steps = 0;
            var hit = false;
            PendingRestore.ApplyPending(here, () =>
            {
                if (steps++ < at) return;
                hit = true;
                throw new IOException("file is locked"); // every step from here on, so the rollback fails too
            });
            if (!hit) break;

            var stopped = Snapshot(here);
            if (!stopped.SequenceEqual(before) && !stopped.SequenceEqual(restored))
            {
                Assert.True(Directory.Exists(Path.Combine(here, "pending-restore")), $"journal lost after a failure at step {at}");
                kept = true;
            }
            PendingRestore.ApplyPending(here); // the obstruction is gone: the next start
            var after = Snapshot(here);
            Assert.True(after.SequenceEqual(before) || after.SequenceEqual(restored), $"mixed after recovery from step {at}");
            Assert.False(Directory.Exists(Path.Combine(here, "pending-restore")));
        }
        Assert.True(kept);
    }

    [Fact]
    public void An_interrupted_restore_whose_new_files_are_damaged_is_rolled_back()
    {
        var restored = Snapshot(Restored(Staged("whole", RestoreMode.Replace)));
        for (var at = 0; ; at++)
        {
            var here = Staged($"damaged-{at}", RestoreMode.Replace);
            var before = Snapshot(here);
            var fault = new Fault(at, () => new Crash());
            try { PendingRestore.ApplyPending(here, fault.Step); }
            catch (Crash) { }
            Assert.True(fault.Hit, "no step left the stores half changed");
            var stopped = Snapshot(here);
            if (stopped.SequenceEqual(before) || stopped.SequenceEqual(restored)) continue;

            File.AppendAllText(Directory.EnumerateFiles(Path.Combine(here, "pending-restore", "next")).First(), " ");
            Assert.Contains("rolled back", PendingRestore.ApplyPending(here));
            Assert.Equal(before, Snapshot(here));
            return;
        }
    }

    /// A data folder with stores of every kind and a backup staged over it: two stores the backup also has, two it lacks, an undo copy and a
    /// safety journal (never touched), and the import's automatic backup (a wipe removes it).
    private string Staged(string name, RestoreMode mode)
    {
        var here = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        Put(here, DataStores.App.FileName, """{"Accent":"Blue"}""");
        Put(here, DataStores.Automation.FileName, """{"Scripts":[{"Id":"a","Name":"Mine"}],"Approved":["mine"]}""");
        Put(here, DataStores.Osd.FileName, """{"Size":3}""");
        Put(here, DataStores.Mouse.FileName, """{"Speed":1}""");
        Put(here, DataStores.BeforeRestore.Named("app").FileName, "{}");
        Put(here, DataStores.PointerOriginal.FileName, """{"Speed":10}""");
        Put(here, BackupFile.BeforeImportName, "zip");
        if (mode == RestoreMode.Wipe)
        {
            PendingRestore.StageWipe(here);
            return here;
        }
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DataStores.App.FileName] = """{"Accent":"Red"}""",
            [DataStores.Automation.FileName] = """{"Scripts":[{"Id":"b","Name":"Theirs"}],"Approved":["evil"]}""",
            [DataStores.Hotkeys.FileName] = """{"Keys":[{"Id":"k"}]}""",
            ["..\\outside.json"] = "{}", // not a store: never staged
        };
        PendingRestore.Stage(here, new BackupContents { AppVersion = "1", Created = DateTime.Now, Areas = [], Files = files }, mode);
        return here;
    }

    private static string Restored(string here)
    {
        Assert.StartsWith("applied", PendingRestore.ApplyPending(here));
        Assert.False(File.Exists(Path.Combine(here, "outside.json")));
        return here;
    }

    private static void Put(string folder, string file, string text) => File.WriteAllText(Path.Combine(folder, file), text);

    /// Every file in the data folder (not the staging folder) and its text, by name.
    private static SortedDictionary<string, string> Snapshot(string folder) =>
        new(Directory.EnumerateFiles(folder).ToDictionary(f => Path.GetFileName(f), File.ReadAllText), StringComparer.Ordinal);
}
