namespace OmniControl.App.Dialogs;

/// <summary>The wording of the questions that come up for many kinds of thing, written once.</summary>
public static class Confirm
{
    /// <summary>"Delete this rule?": <paramref name="name"/> is quoted in the sentence; without one the sentence starts "This rule".</summary>
    public static DialogMessage Delete(string kind, string? name)
    {
        var subject = string.IsNullOrEmpty(name) ? $"This {kind}" : $"\"{name}\"";
        return new DialogMessage($"Delete this {kind}?", $"{subject} will be removed for good.", "Delete");
    }
}
