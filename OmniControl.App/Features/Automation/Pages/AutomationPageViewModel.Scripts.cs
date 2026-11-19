using OmniControl.App.Features.Automation;

namespace OmniControl.App.Features.Automation.Pages;

/// The Scripts tab: the saved scripts on the left, the one chosen on the right (name, language, Run, Import..., Export..., delete and its
/// text). The scripts are <see cref="AutomationService"/>'s; the view only opens the file pickers.
public sealed partial class AutomationPageViewModel
{
    private const string ScriptsTab = "Scripts";

    /// A script file larger than this is not a script anyone typed: it is refused on import.
    private const long MaxScriptBytes = 1024 * 1024;

    public CardGroup ScriptsGroup { get; } = new("Automation/Scripts", "Scripts", string.Empty)
    {
        ExpandedByDefault = true,
        Tip = "Scripts kept inside OmniControl. Each becomes an action, Run script: its name, for Shortcuts, the mouse, gestures and rules. They run as you (never as administrator), hidden, and are stopped after a minute.",
    };

    public ObservableCollection<ScriptItem> Scripts { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedScript))] private ScriptItem? _selectedScript;

    /// One plain sentence after an import or export (where it went, or why it failed); empty otherwise.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasScriptNote))] private string _scriptNote = string.Empty;

    public bool ShowsScripts => Tabs.Current == ScriptsTab;
    public bool NoScripts => Scripts.Count == 0;
    public bool HasSelectedScript => SelectedScript is not null;
    public bool HasScriptNote => ScriptNote.Length > 0;

    /// The file types the Import and Export pickers show.
    public static IReadOnlyList<string> ScriptPatterns => ScriptLanguages.Patterns;

    private IEnumerable<SearchEntry> ScriptSearchEntries =>
        Scripts.Select(s => Entry(s, s.Label, $"script powershell cmd batch python {s.Name}", ScriptsTab));

    private void ShowScriptItems()
    {
        Scripts.Clear();
        foreach (var script in _service.Settings.Scripts) Scripts.Add(new ScriptItem(script, _service));
        SelectedScript = Scripts.FirstOrDefault();
    }

    private void ShowScripts()
    {
        ShowScriptItems();
        Scripts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoScripts));
        _service.ScriptAdded += OnScriptAdded;
        _service.ScriptRemoved += OnScriptRemoved;
    }

    private void ForgetScripts()
    {
        _service.ScriptAdded -= OnScriptAdded;
        _service.ScriptRemoved -= OnScriptRemoved;
    }

    private void OnScriptAdded(Script script)
    {
        var item = new ScriptItem(script, _service);
        Scripts.Add(item);
        SelectedScript = item;
    }

    private void OnScriptRemoved(Script script)
    {
        if (Scripts.FirstOrDefault(s => s.Script == script) is not { } item) return;
        var at = Scripts.IndexOf(item);
        Scripts.Remove(item);
        SelectedScript = Scripts.Count == 0 ? null : Scripts[Math.Min(at, Scripts.Count - 1)];
    }

    [RelayCommand]
    private void AddScript()
    {
        _service.AddScript();
        Tabs.Select(ScriptsTab);
    }

    [RelayCommand]
    private async Task RunScript()
    {
        if (SelectedScript is { } item) await item.RunAsync();
    }

    [RelayCommand]
    private async Task DeleteScript()
    {
        if (SelectedScript is not { } item || !await _ask(Confirm.Delete("script", item.Name))) return;
        _service.RemoveScript(item.Script);
    }

    /// Reads a script file's text into a new script named after the file, in the file's language.
    public async Task ImportScriptAsync(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxScriptBytes)
            {
                ScriptNote = $"\"{Path.GetFileName(path)}\" is too large to be a script.";
                return;
            }
            var body = await File.ReadAllTextAsync(path);
            _service.AddScript(Path.GetFileNameWithoutExtension(path), ScriptLanguages.OfFile(path) ?? ScriptLanguage.PowerShell, body);
            ScriptNote = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScriptNote = $"\"{Path.GetFileName(path)}\" could not be read: {ex.Message}";
        }
    }

    /// The name and ending the Export picker suggests for the chosen script.
    public (string Name, string Extension)? ExportSuggestion =>
        SelectedScript is { } item ? (item.Name, ScriptLanguages.ExtensionOf(item.Script.Language)) : null;

    /// Saves the chosen script's text to <paramref name="path"/>.
    public async Task ExportScriptAsync(string path)
    {
        if (SelectedScript is not { } item) return;
        try
        {
            await File.WriteAllTextAsync(path, item.Body);
            ScriptNote = $"Exported to {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScriptNote = $"The script could not be exported: {ex.Message}";
        }
    }
}
