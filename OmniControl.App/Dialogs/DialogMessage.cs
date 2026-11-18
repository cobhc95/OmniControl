namespace OmniControl.App.Dialogs;

/// <summary>
/// A short message about effects setup for the message window: a title, two or three plain lines, an optional confirm button
/// (Install, Remove), and the technical detail, which is only copied ("Copy details") unless <see cref="ShowDetails"/> asks the
/// window to show it (a preview of changes). When <see cref="Dismiss"/> is cancelled the window closes as if Cancel were chosen
/// (a question that stops mattering, such as "Keep the swapped buttons?" after its time ran out). <see cref="Other"/> is a second
/// answer beside the confirm button (Merge beside Replace everything), asked through <see cref="DialogService.ChooseAsync"/>.
/// <see cref="Input"/> (not null) makes it a one-line question answered through <see cref="DialogService.AskTextAsync"/>.
/// <see cref="RememberKey"/> (a stable kind such as "delete.script", never an item's name) and <see cref="RememberName"/> (what Settings lists it as)
/// make a simple confirmation skippable with "Don't ask again" (<see cref="ConfirmationMemory"/>); errors, choices and anything unsafe leave them null.
/// <see cref="YesNo"/> makes it a Yes/No/Cancel question: <see cref="Confirm"/> starts with "Yes", a "No" button joins it, and the keys Y, N and Esc
/// (their letters underlined) answer it; <see cref="Danger"/> draws the confirm button in red (something is lost or undone).
/// <see cref="Cancel"/> names the button that answers no when "Cancel" would not say it (Keep recording).
/// </summary>
public sealed record DialogMessage(string Title, string Text, string? Confirm = null, string? Details = null, bool ShowDetails = false,
    CancellationToken Dismiss = default, string? Other = null, string? RememberKey = null, string? RememberName = null, string? Input = null,
    bool YesNo = false, bool Danger = false, string? Cancel = null)
{
    /// <summary>True when the window shows the "No" button and the Y / N access keys: a Yes/No question with a confirm button and nothing to type.</summary>
    public bool HasAccessKeys => YesNo && Confirm is not null && !AsksText;

    /// <summary>True when the window asks for a line of text (<see cref="Input"/> is the text it starts with) and <see cref="Text"/> is the muted note under the box.</summary>
    public bool AsksText => Input is not null;

    /// <summary>True when the window offers "Don't ask again": a plain confirmation (a confirm button, no second answer) that gave itself a <see cref="RememberKey"/>.</summary>
    public bool CanRemember => RememberKey is not null && Confirm is not null && Other is null;
}

/// <summary>Which button closed a message: its confirm button, its second answer, or none (Cancel, Esc, the X).</summary>
public enum DialogAnswer { Cancel, Confirm, Other }
