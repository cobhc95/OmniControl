using OmniControl.App.Dialogs;

namespace OmniControl.App.Screenshot;

/// <summary>
/// Where a screenshot or a recording is saved: straight to the action's "Save folder" with its file name pattern, or, while the action's
/// "Ask where to save" is on, wherever the shared save dialog (<see cref="SaveFileWindow"/>) says. "Always save here" is written to the
/// action's own settings (<see cref="SaveKinds.AlwaysSaveHere"/>), so the dialog and the Actions and hotkeys window show the same values.
/// Discard asks first. UI thread.
/// </summary>
internal sealed class SavePlaces
{
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action _saveSettings;

    /// <param name="ask">The shared confirm (<see cref="DialogService.AskAsync"/>).</param>
    /// <param name="saveSettings">Saves the screenshot settings after a save changed the folder (or "Always save here" the switch).</param>
    public SavePlaces(Func<DialogMessage, Task<bool>> ask, Action saveSettings) => (_ask, _saveSettings) = (ask, saveSettings);

    /// <summary>
    /// The full path to save to (its folder may not exist yet), or null when the person discarded it. <paramref name="options"/> is the
    /// action's live set; <paramref name="name"/> has no ending; <paramref name="current"/> is a file already written that may keep its place
    /// (a finished recording), so it does not count as taken.
    /// </summary>
    public async Task<string?> ChooseAsync(SaveKind kind, ScreenshotOptions options, string name, string extension, string? current = null)
    {
        bool Taken(string path) => File.Exists(path) && !string.Equals(Path.GetFullPath(path), current is null ? null : Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);
        if (!SaveKinds.Asks(kind, options)) return current ?? FileNamePattern.UniquePath(SaveKinds.Folder(kind, options), name, extension, Taken);
        var model = new SaveFileModel(SaveKinds.Title(kind), name, extension, SaveKinds.Folder(kind, options));
        while (true)
        {
            if (await new SaveFileWindow(model).AskAsync())
            {
                var folder = model.Folder.Trim();
                if (model.AlwaysHere) SaveKinds.AlwaysSaveHere(kind, options, folder);
                else SaveKinds.RememberFolder(kind, options, folder);
                _saveSettings();
                return FileNamePattern.UniquePath(folder, model.CleanName, extension, Taken);
            }
            if (await _ask(SaveKinds.DiscardQuestion(kind))) return null;
        }
    }
}
