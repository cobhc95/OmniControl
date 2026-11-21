namespace OmniControl.App.Features.Settings.Pages;

/// The plug-in folders on App settings, and the effects' "Keep OmniControl's effects in place" switch: the user's own folders the plug-in scan also searches (<see cref="Shared.Services.AppSettings.PluginFolders"/>,
/// added with the folder picker card, one row each with Remove), and Scan now with what the scan found. Changes count from the next
/// scan, here or in the Add effect window (the same scan).
public sealed partial class AppSettingsPageViewModel
{
    private const string ScanIdleText = "Searches the standard folders and yours, with subfolders.";

    private readonly FileItem _addFolder;
    private readonly ToggleItem? _keepFx;
    private readonly ButtonItem _scan;
    private readonly Features.Volume.Fx.Plugins.PluginLibrary? _plugins;
    private bool _scanning;

    private IEnumerable<SearchEntry> PluginSearchEntries() => _keepFx is null ? Array.Empty<SearchEntry>() :
        new[]
        {
            Entry(_addFolder, PluginsName, "plug-in plugin vst vst2 vst3 folder directory path add other places"),
            Entry(_scan, PluginsName, "plug-in plugin vst vst2 vst3 scan search find refresh"),
            Entry(_keepFx, PluginsName, "equalizer apo config effects keep in place restore put back include editor"),
        }.Concat(PluginItems.OfType<ButtonItem>().Where(IsFolderRow).Select(row => Entry(row, PluginsName, $"plug-in folder remove {row.Description}")));

    private static bool IsFolderRow(ButtonItem item) => item.Label == FolderLabel;

    /// The switch card of "Keep OmniControl's effects in place"; it follows changes made elsewhere (an FX window, a rule).
    private ToggleItem KeepFxCard(Features.Volume.Audio.MixerService mixer)
    {
        var card = new ToggleItem(Features.Volume.Audio.EffectsRules.KeepInPlace, mixer.KeepFxInPlace, mixer.SetKeepFxInPlace) { Tip = Features.Volume.Audio.EffectsRules.KeepInPlaceTip };
        Follow(h => mixer.KeepFxInPlaceChanged += h, h => mixer.KeepFxInPlaceChanged -= h, () => card.Load(mixer.KeepFxInPlace));
        return card;
    }

    private const string FolderLabel = "Scanned folder";

    /// One of the user's folders: its full path, and Remove.
    private ButtonItem FolderRow(string folder)
    {
        ButtonItem row = null!;
        row = new ButtonItem(FolderLabel, "Remove", folder, () => RemoveFolder(row, folder));
        return row;
    }

    /// The folder picker gave a folder: it is added once (the same folder twice is one entry).
    private void AddFolder(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (_settings.PluginFolders.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
        _settings.PluginFolders.Add(full);
        _settings.Save();
        PluginItems.Insert(PluginItems.IndexOf(_scan), FolderRow(full));
        _scan.Description = "Scan now to find its plug-ins.";
    }

    private void RemoveFolder(ButtonItem row, string folder)
    {
        _settings.PluginFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        PluginItems.Remove(row);
        _scan.Description = "Scan now to drop its plug-ins from the list.";
    }

    /// Scan now (and the shell's "Scan for plug-ins" action): one scan at a time; the card's line says how far it got, then what it found.
    public void StartScan() => _ = ScanAsync();

    private async Task ScanAsync()
    {
        if (_scanning || _plugins is not { } plugins) return;
        _scanning = true;
        try
        {
            _scan.Description = "Scanning…";
            await plugins.ScanAsync(new Progress<int>(n => _scan.Description = $"Scanning… {n} found"), CancellationToken.None);
            _scan.Description = plugins.FoundText();
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
