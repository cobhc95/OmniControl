namespace OmniControl.App.Shared.Dialogs;

/// The wording of the questions that come up for many kinds of thing, written once.
public static class Confirm
{
    /// "Delete this rule?": <paramref name="name"/> is quoted in the sentence; without one the sentence starts "This rule".
    /// <param name="rememberKind">The kind the "Don't ask again" memory knows this by when <paramref name="kind"/> varies ("3 devices" is a "device").</param>
    public static DialogMessage Delete(string kind, string? name, string? rememberKind = null)
    {
        var subject = string.IsNullOrEmpty(name) ? $"This {kind}" : $"\"{name}\"";
        return new DialogMessage($"Delete this {kind}?", $"{subject} will be removed for good.", "Delete",
            RememberKey: DeleteKey(rememberKind ?? kind), RememberName: DeleteName(rememberKind ?? kind));
    }

    /// The memory key and Settings name of deleting one kind of thing, so every delete prompt of that kind shares one choice.
    public static string DeleteKey(string kind) => $"delete.{kind.ToLowerInvariant()}";

    public static string DeleteName(string kind) => $"Delete a {kind}";
}
