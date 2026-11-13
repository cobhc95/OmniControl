namespace OmniControl.App.Pages.AppSettingsPage;

/// <summary>
/// The plug-in folders on App settings: the user's own folders the plug-in scan also searches (<see cref="Services.AppSettings.PluginFolders"/>,
/// added with the folder picker card, one row each with Remove), and Scan now with what the scan found. Changes count from the next
/// scan, here or in the Add effect window (the same scan).
/// </summary>
public sealed partial class AppSettingsPageViewModel
{
    private const string FoldersGroup = "Plug-in folders";
    private const string ScanIdleText = "Searches the standard VST3 and VST2 folders and the folders above, with their subfolders.";

    private readonly FileItem _addFolder;
    private readonly ButtonItem _scan;
    private readonly Func<IProgress<int>?, CancellationToken, Task<int>> _scanPlugins;
    private readonly Func<string> _scanResult;
    private bool _scanning;

    private IEnumerable<SearchEntry> PluginSearchEntries() =>
        new[]
        {
            Entry(_addFolder, FoldersGroup, "plug-in plugin vst vst2 vst3 folder directory path add other places"),
            Entry(_scan, FoldersGroup, "plug-in plugin vst vst2 vst3 scan search find refresh"),
        }.Concat(Items.OfType<ButtonItem>().Where(IsFolderRow).Select(row => Entry(row, FoldersGroup, $"plug-in folder remove {row.Description}")));

    private static bool IsFolderRow(ButtonItem item) => item.Label == FolderLabel;

    private const string FolderLabel = "Scanned folder";

    /// <summary>One of the user's folders: its full path, and Remove.</summary>
    private ButtonItem FolderRow(string folder)
    {
        ButtonItem row = null!;
        row = new ButtonItem(FolderLabel, "Remove", folder, () => RemoveFolder(row, folder));
        return row;
    }

    /// <summary>The folder picker gave a folder: it is added once (the same folder twice is one entry).</summary>
    private void AddFolder(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (_settings.PluginFolders.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
        _settings.PluginFolders.Add(full);
        _settings.Save();
        Items.Insert(Items.IndexOf(_scan), FolderRow(full));
        _scan.Description = "Scan now to find the plug-ins in the folder you added.";
    }

    private void RemoveFolder(ButtonItem row, string folder)
    {
        _settings.PluginFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        Items.Remove(row);
        _scan.Description = "Scan now to take that folder's plug-ins off the Add effect list.";
    }

    /// <summary>Scan now (and its hotkey): one scan at a time; the card's line says how far it got, then what it found.</summary>
    private void StartScan() => _ = ScanAsync();

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            _scan.Description = "Scanning…";
            await _scanPlugins(new Progress<int>(n => _scan.Description = $"Scanning… {n} found"), CancellationToken.None);
            _scan.Description = _scanResult();
        }
        catch (IOException ex)
        {
            _scan.Description = $"The scan stopped: {ex.Message}"; // folders are read with their own errors skipped; this is the rest
        }
        finally
        {
            _scanning = false;
        }
    }
}
