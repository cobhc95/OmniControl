namespace OmniControl.App.Hotkeys;

/// <summary>
/// The data of an action that the user edits in the Choose-the-action window's settings pane (a custom action's name, type, target and
/// arguments). <see cref="Read"/> gives the values now; <see cref="Write"/> stores one change at once and returns the action's new title when
/// the change renamed it (null otherwise). With <see cref="Create"/> the entry is the form that makes a new action: its settings are
/// the defaults, the pane's button (<see cref="CreateText"/>) runs it and it returns the action made, already registered.
/// </summary>
public sealed record ActionEditor(IReadOnlyList<ActionOption> Options, Func<IReadOnlyDictionary<string, string>> Read,
    Func<string, string, string?>? Write = null, Func<IReadOnlyDictionary<string, string>, HotkeyAction>? Create = null, string CreateText = "Create");
