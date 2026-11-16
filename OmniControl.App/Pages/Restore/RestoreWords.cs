namespace OmniControl.App.Pages.Restore;

/// <summary>The one question a restore asks: a title, the text and the confirm button.</summary>
public sealed record RestoreQuestion(string Title, string Text, string Button);

/// <summary>The words of "Restore defaults" (pure), the same on every page: the question, the confirmation line and the greyed reason.</summary>
public static class RestoreWords
{
    public const string ButtonText = "Restore defaults";
    public const string AllButtonText = "Restore all";
    public const string AtDefaults = "Already at the defaults";
    public const string NoCopy = "Nothing to undo: the saved copy was not found.";
    public const string Undone = "Your earlier settings are back.";
    public const string Tip = "Put this page's settings back to their defaults (it asks first, and you can undo it)";
    public const string AllTip = "Put every page's settings back to their defaults (it asks first, and you can undo it)";

    private const string CanUndo = "You can undo this.";

    public static string Done(string name) => $"{name} defaults restored";

    public static string Failed(IEnumerable<string> names) => $"Couldn't fully restore {string.Join(", ", names)}; the rest was restored.";

    public static string UndoFailed(IEnumerable<string> names) => $"Couldn't fully bring back {string.Join(", ", names)}; the rest is back.";

    /// <summary>The question for one page; a page whose reset deletes something adds its stronger line and says so on the button.</summary>
    public static RestoreQuestion Ask(IRestorable part) => new($"Restore {part.Name} defaults?",
        $"{part.Scope} {WarningLine(part)}{CanUndo}", part.Warning is null ? "Restore" : "Restore anyway");

    /// <summary>The one question for every page: each page's sentence on a line of its own.</summary>
    public static RestoreQuestion AskAll(IReadOnlyList<IRestorable> parts) => new("Restore all defaults?",
        string.Join(Environment.NewLine, parts.Select(p => $"{p.Name}: {p.Scope} {WarningLine(p)}".TrimEnd()))
        + Environment.NewLine + Environment.NewLine + CanUndo,
        parts.Any(p => p.Warning is not null) ? "Restore anyway" : "Restore all");

    private static string WarningLine(IRestorable part) => part.Warning is { } warning ? $"{warning}. " : string.Empty;
}
