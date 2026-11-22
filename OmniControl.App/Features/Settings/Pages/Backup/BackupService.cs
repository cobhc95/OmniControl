using OmniControl.App.Shared.Services.Backup;
using OmniControl.App.Shared.Ui.SettingsImport;

namespace OmniControl.App.Features.Settings.Pages.Backup;

/// The Backup and restore card's three actions: Export... and Import... open the shared settings window (choose areas, then a file is
/// written, or the file's areas are imported live with Undo) and Wipe all... (asks twice), which is staged and carried out as OmniControl
/// restarts (<see cref="PendingRestore"/>, <see cref="AppRestart"/>). Each failure is one plain sentence.
public sealed class BackupService
{
    private static readonly string[] Patterns = { "*." + BackupFile.Extension };
    private const string TypeName = "OmniControl backup";

    private readonly Func<DialogMessage, Task<DialogAnswer>> _choose;
    private readonly Action<string> _report;
    private readonly Features.OtherPcs.SettingsImport.ImportFromPcService? _importFromPc;
    private readonly ImportHooks _hooks;
    private readonly SingleWindow<SettingsWindow> _window;

    /// <param name="choose">Shows a message with up to two answers (the shared <see cref="DialogService"/>).</param>
    /// <param name="report">Shows one plain sentence in the status line.</param>
    /// <param name="importFromPc">Opens "Import settings from a PC" (the Import from another PC card); null while Other PCs is off.</param>
    /// <param name="hooks">How each store's owner reloads it live after an import.</param>
    public BackupService(Func<DialogMessage, Task<DialogAnswer>> choose, Action<string> report, Features.OtherPcs.SettingsImport.ImportFromPcService? importFromPc, ImportHooks hooks)
    {
        _hooks = hooks;
        _window = new SingleWindow<SettingsWindow>(report);
        _choose = choose;
        _report = report;
        _importFromPc = importFromPc;
    }

    /// Opens "Export settings": the areas saved here; the ticked ones go into one .omnibackup file chosen after.
    public void Export() => _window.Show(
        () => new SettingsWindow { DataContext = new SettingsWindowViewModel(new HereSource(), ExportAsync) },
        closed => (closed.DataContext as IDisposable)?.Dispose(), "Couldn't open the export window");

    /// Opens "Import settings from <file>" for a backup file the person picks: only the areas it holds are listed, the ticked ones are
    /// imported live under the import journal, with a backup first and Undo (<see cref="SettingsImporter"/>).
    public async Task ImportAsync()
    {
        if (await PathPicker.PickAsync(null, PathKind.File, "Import a backup", typeName: TypeName, patterns: Patterns) is not { } path) return;
        _window.Show(() => new SettingsWindow { DataContext = new SettingsWindowViewModel(new FileSource(path), _hooks) },
            closed => (closed.DataContext as IDisposable)?.Dispose(), "Couldn't open the import window");
    }

    private static async Task<string?> ExportAsync(IReadOnlyList<ImportArea> areas)
    {
        var name = $"{AppInfo.Name} {DateTime.Now:yyyy-MM-dd}";
        if (await PathPicker.PickSaveAsync(null, "Export a backup", name, BackupFile.Extension, TypeName, Patterns) is not { } path) return null;
        try
        {
            await Task.Run(() =>
            {
                AppDataStore.FlushSaves(); // the backup reads the files: waiting saves first
                BackupFile.Write(AppDataStore.Folder, path, AppInfo.Version, areas.SelectMany(a => a.Stores));
            });
            return $"Exported {areas.Count} {(areas.Count == 1 ? "area" : "areas")} to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"The backup could not be written: {ex.Message}";
        }
    }

    /// What is saved on this PC, as the export lists it.
    private sealed class HereSource : ISettingsSource
    {
        public string Name => AppInfo.Name;

        public IReadOnlyList<ImportArea> Areas => ImportAreas.All;

        public Task<(IReadOnlyList<AreaOffer>? Offers, string? Why)> OffersAsync() => Task.Run(() =>
        {
            AppDataStore.FlushSaves();
            return ((IReadOnlyList<AreaOffer>?)ImportAreas.Offers(ImportAreas.All, AppDataStore.LoadText), (string?)null);
        });
    }

    /// A backup file as the import's source: read and checked once, off the UI thread.
    private sealed class FileSource(string path) : IImportSource
    {
        private BackupContents? _contents;

        public string Name { get; } = Path.GetFileName(path);

        public IReadOnlyList<ImportArea> Areas => ImportAreas.All;

        public async Task<(IReadOnlyList<AreaOffer>? Offers, string? Why)> OffersAsync()
        {
            var (contents, problem) = await Task.Run(() => BackupFile.Read(path));
            if ((_contents = contents) is null) return (null, problem ?? "That file is not an OmniControl backup.");
            return (ImportAreas.Offers(ImportAreas.All, s => contents.Files.GetValueOrDefault(s.FileName)), null);
        }

        public Task<(IReadOnlyDictionary<string, string>? Files, string? Why)> AreaAsync(string area) =>
            Task.FromResult<(IReadOnlyDictionary<string, string>?, string?)>(_contents is null ? (null, "The file could not be read.") : (_contents.Files, null));
    }
    /// The paired PCs that are connected now: the only ones a settings import can read from.
    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => _importFromPc?.OnlinePcs() ?? Array.Empty<(string, string)>();

    /// A settings import from a paired PC can be offered (Other PCs runs).
    public bool CanImportFromPc => _importFromPc is not null;

    /// Opens "Import settings from a PC" for the paired PC.
    public void ImportFromPc(string pcId) => _importFromPc?.Open(pcId);

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
}
