using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Automation;

/// The one description of a custom action's settings (name, type, target, arguments): what the Choose-the-action window shows, and how a value
/// is read from and applied to a <see cref="CustomAction"/>. The Custom actions tab edits the same fields through <see cref="Apply"/>, so there is one rule for each.
public static class CustomFields
{
    public const string Name = "name", Kind = "kind", Arguments = "arguments";

    // One target setting per way it is picked (typed words, a file, a program), all stored as the action's one target.
    public const string TargetText = "target.text", TargetFile = "target.file", TargetProgram = "target.program", TargetSendKeys = "target.keys";

    private static readonly string[] TargetKeys = { TargetText, TargetFile, TargetProgram, TargetSendKeys };

    private static readonly ActionOption NameOption = ActionOption.Text(Name, "Name");
    private static readonly ActionOption KindOption = ActionOption.Choice(Kind, "Type", CustomKinds.Names);

    public static IReadOnlyList<ActionOption> Options { get; } = new[]
    {
        NameOption,
        KindOption,
        ActionOption.Text(TargetText, "Target", hint: "Command or address").ShownWhen(KindOption, CustomKinds.NamesOf(CustomKinds.Typed)),
        ActionOption.Path(TargetFile, "Target", PathKind.File, "File or folder").ShownWhen(KindOption, CustomKinds.NamesOf(CustomKinds.Files)),
        ActionOption.Path(TargetProgram, "Target", PathKind.Program, "Program").ShownWhen(KindOption, CustomKinds.NamesOf(CustomKinds.Programs)),
        ActionOption.Text(TargetSendKeys, "Keys to send", hint: CustomKinds.HintOf(CustomKind.Keys)).CheckedBy(MouseOptions.KeysProblem)
            .ShownWhen(KindOption, CustomKinds.NameOf(CustomKind.Keys)),
        ActionOption.Text(Arguments, "Arguments").ShownWhen(KindOption, CustomKinds.NamesOf(CustomKinds.WithArguments)),
    };

    /// The values of every setting for this action.
    public static IReadOnlyDictionary<string, string> Read(CustomAction custom)
    {
        var values = new Dictionary<string, string>
        {
            [Name] = custom.Name,
            [Kind] = CustomKinds.NameOf(custom.Kind),
            [Arguments] = custom.Arguments,
        };
        foreach (var key in TargetKeys) values[key] = custom.Target;
        return values;
    }

    /// What the action starts, as approved once (<see cref="AutomationService.Approvals"/>): its kind, target and arguments; null for a kind
    /// that starts nothing of its own (a web address, keys).
    public static string? Starts(CustomAction custom) => custom.Kind is CustomKind.Url or CustomKind.Keys ? null
        : $"custom|{custom.Kind}|{custom.Target.Trim()}|{custom.Arguments}";

    /// What the approval question shows: exactly what will be started.
    public static string Describe(CustomAction custom) =>
        $"{CustomKinds.NameOf(custom.Kind)}: {custom.Target.Trim()}{(custom.Arguments.Length > 0 ? " " + custom.Arguments : string.Empty)}";

    /// Stores one setting on the action. Returns false when the value is not kept (a blank name).
    public static bool Apply(CustomAction custom, string key, string value)
    {
        switch (key)
        {
            case Name:
                if (value.Trim() is not { Length: > 0 } name) return false;
                custom.Name = name;
                break;
            case Kind: custom.Kind = CustomKinds.KindOf(value); break;
            case Arguments: custom.Arguments = value.Trim(); break;
            case var target when TargetKeys.Contains(target): custom.Target = value.Trim(); break;
        }
        return true;
    }
}
