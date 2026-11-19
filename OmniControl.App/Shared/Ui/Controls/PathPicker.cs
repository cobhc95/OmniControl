using Avalonia.Platform.Storage;

namespace OmniControl.App.Shared.Ui.Controls;

/// The one place that opens a file or folder picker, or a "Save as" picker, (Avalonia's storage provider) for a path: the shared <see cref="PathField"/>, the file
/// cards and the pop-downs that browse all come here. It starts in the folder of the path already given.
public static class PathPicker
{
    /// What counts as a program in a file picker.
    public static readonly string[] ProgramPatterns = { "*.exe", "*.bat", "*.cmd", "*.lnk" };

    /// "Choose a program" for the pop-downs and cards that add one: the chosen path, or null when cancelled. <paramref name="exeOnly"/> narrows the list to .exe files.
    public static Task<string?> PickProgramAsync(Visual from) =>
        PickAsync(from, PathKind.Program, "Choose a program", typeName: "Programs", patterns: ProgramPatterns);

    /// The chosen path, or null when the user cancelled. Without <paramref name="from"/> it opens over the main window. <paramref name="patterns"/> narrow the files shown (with <paramref name="typeName"/>); none = the kind's own.
    public static async Task<string?> PickAsync(Visual? from, PathKind kind, string title, string? current = null,
        string? typeName = null, IReadOnlyList<string>? patterns = null)
    {
        if (TopLevel.GetTopLevel(from ?? OwnedWindow.MainWindow)?.StorageProvider is not { } storage) return null;
        var start = await StartFolderAsync(storage, current);
        if (kind == PathKind.Folder)
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = FilterFor(kind, typeName, patterns),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// "Save as": the chosen path, or null when cancelled. Windows asks before replacing a file that is there.
    public static async Task<string?> PickSaveAsync(Visual? from, string title, string suggestedName, string extension, string typeName,
        IReadOnlyList<string> patterns)
    {
        if (TopLevel.GetTopLevel(from ?? OwnedWindow.MainWindow)?.StorageProvider is not { } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = patterns } },
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    private static IReadOnlyList<FilePickerFileType>? FilterFor(PathKind kind, string? typeName, IReadOnlyList<string>? patterns)
    {
        if (patterns is { Count: > 0 }) return new[] { new FilePickerFileType(typeName ?? "Files") { Patterns = patterns } };
        return kind == PathKind.Program ? new[] { new FilePickerFileType("Programs") { Patterns = ProgramPatterns }, FilePickerFileTypes.All } : null;
    }

    /// The folder of the path already in the box, when it is a real one (a path with quotes or a web address gives none).
    private static async Task<IStorageFolder?> StartFolderAsync(IStorageProvider storage, string? current)
    {
        var path = current?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return null;
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return folder is { Length: > 0 } && Directory.Exists(folder) ? await storage.TryGetFolderFromPathAsync(folder) : null;
    }
}
