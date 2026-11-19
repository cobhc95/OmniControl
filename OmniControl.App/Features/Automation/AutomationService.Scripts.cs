using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation;

/// The saved scripts (the Scripts tab): each is kept in <see cref="AutomationSettings.Scripts"/> and registers one master action,
/// "Run script: name", so Shortcuts, the mouse and gesture pickers, the action window and every rule can run it. They run through the
/// same <see cref="CustomActionRunner"/> as custom actions.
public sealed partial class AutomationService
{
    private const string ScriptTitlePrefix = "Run script: ";
    private readonly Lazy<string?> _python = new(PythonLocator.Find);

    /// An installed Python was found, so Python scripts can be written and run.
    public bool HasPython => _python.Value is not null;

    /// A script was made, edited or removed, so the tab follows.
    public event Action<Script>? ScriptAdded, ScriptRemoved;

    /// A new script at the end of the list, with a name of its own (and, when given, a name, language and text from an imported file).
    public Script AddScript(string? name = null, ScriptLanguage language = ScriptLanguage.PowerShell, string body = "")
    {
        var script = new Script { Name = name ?? $"Script {Settings.Scripts.Count + 1}", Language = language, Body = body };
        Settings.Scripts.Add(script);
        _hotkeys.Register(MakeScriptAction(script));
        Settings.Save();
        ScriptAdded?.Invoke(script);
        return script;
    }

    public void RemoveScript(Script script)
    {
        Settings.Scripts.Remove(script);
        _hotkeys.Unregister(script.ActionId);
        Settings.Save();
        ScriptRemoved?.Invoke(script);
    }

    /// A new name makes its action again; its shortcut and every use of it follow the id, so they are kept.
    public void RenameScript(Script script, string name)
    {
        if (script.Name == name) return;
        script.Name = name;
        _hotkeys.Unregister(script.ActionId);
        _hotkeys.Register(MakeScriptAction(script));
        Settings.Save();
    }

    /// The language or the text changed: saved at once.
    public void ScriptEdited() => Settings.Save();

    /// Runs a script now; null when it worked, otherwise the plain sentence that was also shown in the status line.
    public async Task<string?> RunScriptAsync(Script script)
    {
        if (await _custom.RunScriptAsync(script, _python.Value) is not { } problem) return null;
        _report($"{problem} Details are in {LogFile}.");
        return problem;
    }

    private HotkeyAction MakeScriptAction(Script script) =>
        new(script.ActionId, ScriptTitlePrefix + (script.Name.Length > 0 ? script.Name : "Script"), ActionSections.Automation,
            $"script run powershell cmd batch python {script.Name}", ids => _ = RunScriptAsync(script), group: "Scripts")
        {
            PickerSection = ActionSections.CustomActions,
        };
}
