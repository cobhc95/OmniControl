using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Automation;

namespace OmniControl.App.Pages.Automation;

/// <summary>
/// One saved script on the Scripts tab: its name, language and text, each saved as it changes, and the one thin line saying how the
/// last run (since OmniControl started) went. Its action ("Run script: name") is <see cref="AutomationService"/>'s.
/// </summary>
public sealed partial class ScriptItem : ObservableObject
{
    private readonly AutomationService _service;

    public Script Script { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _languageName;
    [ObservableProperty] private string _body;

    /// <summary>"Last run 14:02: worked", or what went wrong; empty until it has run.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLastRun))] private string _lastRun = string.Empty;
    [ObservableProperty] private bool _lastRunFailed;

    public bool HasLastRun => LastRun.Length > 0;

    /// <summary>The languages offered: Python only when an installed Python was found (or this script already is one).</summary>
    public IReadOnlyList<string> LanguageNames { get; }

    public string Label => Script.Name;

    public string LanguageTag => ScriptLanguages.NameOf(Script.Language);

    public ScriptItem(Script script, AutomationService service)
    {
        Script = script;
        _service = service;
        _name = script.Name;
        _languageName = ScriptLanguages.NameOf(script.Language);
        _body = script.Body;
        LanguageNames = ScriptLanguages.Offered(service.HasPython || script.Language == ScriptLanguage.Python)
            .Select(ScriptLanguages.NameOf).ToList();
    }

    partial void OnNameChanged(string value)
    {
        if (value.Trim().Length == 0) return; // a blank name is not kept, as for a custom action
        _service.RenameScript(Script, value.Trim());
        OnPropertyChanged(nameof(Label));
    }

    partial void OnLanguageNameChanged(string value)
    {
        Script.Language = ScriptLanguages.OfName(value);
        _service.ScriptEdited();
        OnPropertyChanged(nameof(LanguageTag));
    }

    partial void OnBodyChanged(string value)
    {
        Script.Body = value;
        _service.ScriptEdited();
    }

    public async Task RunAsync()
    {
        var problem = await _service.RunScriptAsync(Script);
        LastRunFailed = problem is not null;
        LastRun = $"Last run {DateTime.Now:t}: {problem ?? "it worked."}";
    }
}
