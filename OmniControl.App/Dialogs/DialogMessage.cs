namespace OmniControl.App.Dialogs;

/// <summary>
/// A short message about effects setup for the message window: a title, two or three plain lines, an optional confirm button
/// (Install, Remove), and the technical detail, which is only copied ("Copy details") unless <see cref="ShowDetails"/> asks the
/// window to show it (a preview of changes). When <see cref="Dismiss"/> is cancelled the window closes as if Cancel were chosen
/// (a question that stops mattering, such as "Keep the swapped buttons?" after its time ran out). <see cref="Other"/> is a second
/// answer beside the confirm button (Merge beside Replace everything), asked through <see cref="DialogService.ChooseAsync"/>.
/// <see cref="RememberKey"/> (a stable kind such as "delete.script", never an item's name) and <see cref="RememberName"/> (what Settings lists it as)
/// make a simple confirmation skippable with "Don't ask again" (<see cref="ConfirmationMemory"/>); errors, choices and anything unsafe leave them null.
/// </summary>
public sealed record DialogMessage(string Title, string Text, string? Confirm = null, string? Details = null, bool ShowDetails = false,
    CancellationToken Dismiss = default, string? Other = null, string? RememberKey = null, string? RememberName = null)
{
    /// <summary>True when the window offers "Don't ask again": a plain confirmation (a confirm button, no second answer) that gave itself a <see cref="RememberKey"/>.</summary>
    public bool CanRemember => RememberKey is not null && Confirm is not null && Other is null;
}

/// <summary>Which button closed a message: its confirm button, its second answer, or none (Cancel, Esc, the X).</summary>
public enum DialogAnswer { Cancel, Confirm, Other }
