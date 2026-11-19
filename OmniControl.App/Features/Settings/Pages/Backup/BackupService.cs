using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Features.Settings.Pages.Backup;

/// The Backup and restore card's three actions: Export... (one .omnibackup file of every saved store), Import... (shows what the file
/// holds, then Replace everything or Merge) and Wipe all... (asks twice). An import or wipe is staged and carried out as OmniControl
/// restarts (<see cref="PendingRestore"/>, <see cref="AppRestart"/>). Each failure is one plain sentence in the status line.
public sealed class BackupService
{
    private static readonly string[] Patterns = { "*." + BackupFile.Extension };
    private const string TypeName = "OmniControl backup";

    private readonly Func<DialogMessage, Task<DialogAnswer>> _choose;
    private readonly Action<string> _report;
    private readonly Features.OtherPcs.SettingsImport.ImportFromPcService _importFromPc;

    /// <param name="choose">Shows a message with up to two answers (the shared <see cref="DialogService"/>).</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    /// <param name="importFromPc">Opens "Import settings from a PC" (the Import from another PC card).</param>
    public BackupService(Func<DialogMessage, Task<DialogAnswer>> choose, Action<string> report, Features.OtherPcs.SettingsImport.ImportFromPcService importFromPc)
    {
        _choose = choose;
        _report = report;
        _importFromPc = importFromPc;
    }

    public async Task ExportAsync()
    {
        var name = $"{AppInfo.Name} {DateTime.Now:yyyy-MM-dd}";
        if (await PathPicker.PickSaveAsync(null, "Export a backup", name, BackupFile.Extension, TypeName, Patterns) is not { } path) return;
        try
        {
            var count = await Task.Run(() =>
            {
                AppDataStore.FlushSaves(); // the backup reads the files: waiting saves first
                return BackupFile.Write(AppDataStore.Folder, path, AppInfo.Version);
            });
            _report($"Backed up {count} {(count == 1 ? "area" : "areas")} to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _report($"The backup could not be written: {ex.Message}");
        }
    }

    public async Task ImportAsync()
    {
        if (await PathPicker.PickAsync(null, PathKind.File, "Import a backup", typeName: TypeName, patterns: Patterns) is not { } path) return;
        var (contents, problem) = await Task.Run(() => BackupFile.Read(path));
        if (contents is null)
        {
            _report(problem ?? "That file is not an OmniControl backup.");
            return;
        }
        var answer = await _choose(new DialogMessage("Import this backup?",
            $"Made by OmniControl {contents.AppVersion} on {contents.Created:d}. Merge adds what you don't have and keeps your own settings; " +
            "Replace everything puts the backup back as it was. OmniControl restarts to apply it.",
            Confirm: "Replace everything", Details: Describe(contents), ShowDetails: true, Other: "Merge"));
        if (answer == DialogAnswer.Cancel) return;
        Stage(() => PendingRestore.Stage(AppDataStore.Folder, contents, answer == DialogAnswer.Other ? RestoreMode.Merge : RestoreMode.Replace));
    }

    /// The paired PCs that are connected now: the only ones a settings import can read from.
    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => _importFromPc.OnlinePcs();

    /// Opens "Import settings from a PC" for the paired PC.
    public void ImportFromPc(string pcId) => _importFromPc.Open(pcId);

    public async Task WipeAsync()
    {
        if (await _choose(new DialogMessage("Wipe all saved data?",
                "Every setting, preset, shortcut, rule, script and list OmniControl saved is deleted, and it restarts with factory settings.",
                "Wipe all")) != DialogAnswer.Confirm) return;
        if (await _choose(new DialogMessage("Wipe everything now?",
                "This can't be undone. Export a backup first if you may want any of it back.", "Wipe and restart")) != DialogAnswer.Confirm) return;
        Stage(() => PendingRestore.StageWipe(AppDataStore.Folder));
    }

    private void Stage(Action stage)
    {
        try
        {
            stage();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _report($"Nothing was changed: the data folder could not be written ({ex.Message}).");
            return;
        }
        AppLifecycle.RequestRestart();
    }

    /// One line per area: its name, how many entries, and whether an import merges it or replaces it.
    private static string Describe(BackupContents contents) => string.Join(Environment.NewLine, contents.Areas.Select(a =>
        $"{a.Store.Area}{(a.Count is { } n ? $": {n} {(n == 1 ? "entry" : "entries")}" : string.Empty)}" +
        (a.Store.Mergeable ? string.Empty : " (Merge replaces this area)")));
}
