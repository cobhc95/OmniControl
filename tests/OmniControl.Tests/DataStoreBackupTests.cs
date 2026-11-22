using System.Text.Json.Nodes;
using OmniControl.App.Features.Automation;
using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.Tests;

/// The master list of saved stores, and backup, import (replace and merge) and wipe through it, all in a temporary folder.
public sealed class DataStoreBackupTests : IDisposable
{
    private readonly string _here = Directory.CreateTempSubdirectory("omni-here").FullName;
    private readonly string _work = Directory.CreateTempSubdirectory("omni-work").FullName;

    public void Dispose()
    {
        Directory.Delete(_here, recursive: true);
        Directory.Delete(_work, recursive: true);
    }

    private void Put(string folder, DataStore store, string json) => File.WriteAllText(Path.Combine(folder, store.FileName), json);

    private JsonNode Read(string folder, DataStore store) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, store.FileName)))!;

    [Fact]
    public void Every_store_has_its_own_file_and_safety_files_are_never_backed_up()
    {
        Assert.Equal(DataStores.All.Count, DataStores.All.Select(s => s.FileName.ToLowerInvariant()).Distinct().Count());
        Assert.DoesNotContain(DataStores.BackedUp, s => s.Kind != StoreKind.Saved);
        Assert.Contains(DataStores.BackedUp, s => s == DataStores.Automation);
    }

    [Fact]
    public void A_backup_holds_the_saved_stores_and_never_logs_or_journals()
    {
        Put(_here, DataStores.App, """{"Accent":"Blue"}""");
        Put(_here, DataStores.Automation, """{"Rules":[],"Customs":[],"Scripts":[{"Id":"a","Name":"One"}],"Approved":["x"]}""");
        Put(_here, DataStores.EngineJournal, "[]");
        File.WriteAllText(Path.Combine(_here, "automation-log.txt"), "log");
        var file = Path.Combine(_work, "b." + BackupFile.Extension);

        Assert.Equal(2, BackupFile.Write(_here, file, "9.9"));
        var (contents, problem) = BackupFile.Read(file);

        Assert.Null(problem);
        Assert.Equal("9.9", contents!.AppVersion);
        Assert.Equal(new[] { DataStores.App.FileName, DataStores.Automation.FileName }.Order(), contents.Files.Keys.Order());
        Assert.Equal(1, contents.Areas.Single(a => a.Store == DataStores.Automation).Count); // the script; the approvals are not entries
    }

    [Fact]
    public void A_backup_too_large_to_read_back_is_refused_and_the_previous_one_is_kept()
    {
        Put(_here, DataStores.App, """{"Accent":"Blue"}""");
        var file = Path.Combine(_work, "b." + BackupFile.Extension);
        BackupFile.Write(_here, file, "1");
        var previous = File.ReadAllBytes(file);

        Put(_here, DataStores.App, "{\"Big\":\"" + new string('a', (int)BackupFile.MaxBytes + 1) + "\"}");

        Assert.Throws<InvalidDataException>(() => BackupFile.Write(_here, file, "2"));
        Assert.Equal(previous, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".partial"));
    }

    [Fact]
    public void Replace_puts_the_backup_back_keeps_own_approvals_and_resets_what_it_lacks()
    {
        Put(_work, DataStores.Automation, """{"Scripts":[{"Id":"b","Name":"Theirs"}],"Approved":["evil"]}""");
        var file = Backup(_work);
        Put(_here, DataStores.Automation, """{"Scripts":[{"Id":"a","Name":"Mine"}],"Approved":["mine"]}""");
        Put(_here, DataStores.Osd, """{"Size":3}""");
        Put(_here, DataStores.PointerOriginal, """{"Speed":10}""");

        PendingRestore.Stage(_here, BackupFile.Read(file).Contents!, RestoreMode.Replace);
        Assert.NotNull(PendingRestore.ApplyPending(_here));

        var automation = Read(_here, DataStores.Automation);
        Assert.Equal("Theirs", (string?)automation["Scripts"]![0]!["Name"]);
        Assert.Equal("mine", (string?)automation["Approved"]![0]);
        Assert.False(File.Exists(Path.Combine(_here, DataStores.Osd.FileName)));
        Assert.True(File.Exists(Path.Combine(_here, DataStores.PointerOriginal.FileName)));
        Assert.Null(PendingRestore.ApplyPending(_here)); // carried out once only
    }

    [Fact]
    public void Merge_adds_what_is_missing_and_keeps_what_is_here()
    {
        Put(_work, DataStores.Automation, """{"Scripts":[{"Id":"a","Name":"Old name"},{"Id":"b","Name":"New"}]}""");
        Put(_work, DataStores.Osd, """{"Size":9}""");
        var file = Backup(_work);
        Put(_here, DataStores.Automation, """{"Scripts":[{"Id":"a","Name":"Mine"}]}""");
        Put(_here, DataStores.Osd, """{"Size":3}""");
        Put(_here, DataStores.Mouse, """{"Speed":1}""");

        PendingRestore.Stage(_here, BackupFile.Read(file).Contents!, RestoreMode.Merge);
        PendingRestore.ApplyPending(_here);

        var scripts = Read(_here, DataStores.Automation)["Scripts"]!.AsArray();
        Assert.Equal(new[] { "Mine", "New" }, scripts.Select(s => (string?)s!["Name"]));
        Assert.Equal(9, (int)Read(_here, DataStores.Osd)["Size"]!); // the OSD can't merge: the backup's replaces it
        Assert.Equal(1, (int)Read(_here, DataStores.Mouse)["Speed"]!); // not in the backup: kept
    }

    [Fact]
    public void Wipe_deletes_saved_stores_and_undo_copies_but_keeps_safety_journals()
    {
        Put(_here, DataStores.App, "{}");
        Put(_here, DataStores.BeforeRestore.Named("app"), "{}");
        Put(_here, DataStores.TrackpadOriginal, "{}");

        PendingRestore.StageWipe(_here);
        PendingRestore.ApplyPending(_here);

        Assert.Equal(new[] { DataStores.TrackpadOriginal.FileName }, Directory.EnumerateFiles(_here).Select(Path.GetFileName));
    }

    [Fact]
    public void A_file_that_is_not_a_backup_gives_one_sentence()
    {
        var file = Path.Combine(_work, "bad." + BackupFile.Extension);
        File.WriteAllText(file, "not a zip");
        var (contents, problem) = BackupFile.Read(file);
        Assert.Null(contents);
        Assert.EndsWith(".", problem);
    }

    [Fact]
    public void Script_languages_follow_the_file_ending_and_python_is_offered_only_when_found()
    {
        Assert.Equal(ScriptLanguage.Batch, ScriptLanguages.OfFile(@"C:\x\a.BAT"));
        Assert.Null(ScriptLanguages.OfFile("a.txt"));
        Assert.DoesNotContain(ScriptLanguage.Python, ScriptLanguages.Offered(hasPython: false));
        Assert.Contains(ScriptLanguage.Python, ScriptLanguages.Offered(hasPython: true));
    }

    private string Backup(string folder)
    {
        var file = Path.Combine(Path.GetTempPath(), $"omni-{Guid.NewGuid():N}.{BackupFile.Extension}");
        BackupFile.Write(folder, file, "1");
        File.Move(file, Path.Combine(_work, Path.GetFileName(file)));
        foreach (var store in Directory.EnumerateFiles(folder, "*.json")) File.Delete(store);
        return Path.Combine(_work, Path.GetFileName(file));
    }
}
