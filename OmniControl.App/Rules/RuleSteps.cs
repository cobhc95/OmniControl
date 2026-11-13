using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Services;

namespace OmniControl.App.Rules;

/// <summary>
/// What a rule's "then" steps can be: the mouse button action list (<see cref="MouseActionCatalog.Choices"/> over the live master
/// list of OmniControl actions: OmniControl actions, Windows functions, media keys, send a shortcut, open a program, file or link,
/// open a folder), without what needs a held button (modes, layers, Default, Disabled) and without the press/release/repeat and
/// "also send" settings, plus the two steps only a sequence has: run a command, and wait.
/// </summary>
public static class RuleSteps
{
    public const string Command = "script", Wait = "wait";
    private const string Category = "Commands and timing";

    public static readonly ActionOption CommandText = ActionOption.Text("command", "Command", "", "Start-Process notepad");
    public static readonly ActionOption Shell = ActionOption.Choice("shell", "Run with", new[] { ScriptRunner.PowerShell, ScriptRunner.Cmd });
    public static readonly ActionOption Hidden = ActionOption.Toggle("hidden", "Hidden window", on: true);
    public static readonly ActionOption Timeout =
        ActionOption.Number("timeout", "Stop it after", (int)Timing.RuleCommandTimeout.TotalSeconds, 1, 600, 1, " s");
    public static readonly ActionOption WaitTime = ActionOption.Number("ms", "Wait for", (int)Timing.RuleWait.TotalMilliseconds, 50, 10000, 50, " ms");

    // Settings that only mean something for a held mouse button, or that a rule does as a step of its own.
    private static readonly HashSet<string> NotInRules = new[]
    {
        MouseOptions.Trigger, MouseOptions.Times, MouseOptions.RepeatDelay, MouseOptions.RepeatEvery,
        MouseOptions.KeepOriginal, MouseOptions.AlsoSend, MouseOptions.Administrator,
    }.Select(o => o.Key).ToHashSet();

    private static readonly PickChoice[] Own =
    {
        new(Command, Category, "Run a PowerShell or CMD command", Keywords: "script powershell cmd batch terminal console",
            Options: new[] { CommandText, Shell, Hidden, Timeout }, AskFirst: true),
        new(Wait, Category, "Wait", Keywords: "pause delay sleep", Options: new[] { WaitTime }, AskFirst: true),
    };

    /// <summary>The picker's list, read from the live master list each time.</summary>
    public static IReadOnlyList<PickChoice> Choices(IEnumerable<HotkeyAction> actions) =>
        MouseActionCatalog.Choices(actions).Where(Usable).Select(c => c with { Options = Trim(c.Options) }).Concat(Own).ToList();

    private static bool Usable(PickChoice choice)
    {
        var id = MouseActionCatalog.Split(choice.Spec).Id;
        return id is not (MouseActionCatalog.Default or MouseActionCatalog.Disabled) && !MouseActionCatalog.IsMode(id) && !MouseActionCatalog.IsLayerAction(id);
    }

    private static IReadOnlyList<ActionOption>? Trim(IReadOnlyList<ActionOption>? options) =>
        options?.Where(o => !NotInRules.Contains(o.Key)).ToList() is { Count: > 0 } left ? left : null;

    /// <summary>True for a step that runs a command or opens a program: it needs the user's approval before it is saved or run.</summary>
    public static bool NeedsApproval(string stored) => MouseAction.Parse(stored).Id is Command or MouseActionCatalog.Run;

    /// <summary>Exactly what a step that <see cref="NeedsApproval"/> will start, as the confirm window shows it.</summary>
    public static string Describe(string stored)
    {
        var step = MouseAction.Parse(stored);
        if (step.Id == Command) return ScriptRunner.Describe(step.Options.Text(Shell), step.Options.Text(CommandText));
        var arguments = step.Options.Text(MouseOptions.Arguments);
        var folder = step.Options.Text(MouseOptions.StartIn);
        return step.Param + (arguments.Length > 0 ? " " + arguments : string.Empty) + (folder.Length > 0 ? $" (in {folder})" : string.Empty);
    }

    /// <summary>How a stored step reads in the rule's sentence ("Lock workstation", "wait 500 ms", "run Start-Process notepad").</summary>
    public static string PhraseOf(string stored, IReadOnlyList<PickChoice> choices)
    {
        var step = MouseAction.Parse(stored);
        switch (step.Id)
        {
            case Wait: return $"wait {step.Options.Int(WaitTime)} ms";
            case Command: return $"run {step.Options.Text(CommandText)}";
        }
        var action = MouseAction.Split(stored).Action;
        if (choices.FirstOrDefault(c => c.Matches(action)) is not { } choice) return "(an action that is no longer available)";
        return choice.ParamHint is null ? choice.Name : $"{choice.Name}: {choice.ParameterOf(action)}";
    }
}
