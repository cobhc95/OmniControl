namespace OmniControl.App.Dialogs;

/// <summary>
/// A short message about effects setup for the message window: a title, two or three plain lines, an optional confirm button
/// (Install, Remove), and the technical detail, which is only copied ("Copy details") unless <see cref="ShowDetails"/> asks the
/// window to show it (a preview of changes). When <see cref="Dismiss"/> is cancelled the window closes as if Cancel were chosen
/// (a question that stops mattering, such as "Keep the swapped buttons?" after its time ran out). <see cref="Other"/> is a second
/// answer beside the confirm button (Merge beside Replace everything), asked through <see cref="DialogService.ChooseAsync"/>.
/// </summary>
public sealed record DialogMessage(string Title, string Text, string? Confirm = null, string? Details = null, bool ShowDetails = false,
    CancellationToken Dismiss = default, string? Other = null);

/// <summary>Which button closed a message: its confirm button, its second answer, or none (Cancel, Esc, the X).</summary>
public enum DialogAnswer { Cancel, Confirm, Other }
