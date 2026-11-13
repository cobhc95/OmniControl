namespace OmniControl.App.Audio.Engine;

/// <summary>
/// A short message about effects setup for the message window: a title, two or three plain lines, an optional confirm button
/// (Install, Remove), and the technical detail, which is only copied ("Copy details") unless <see cref="ShowDetails"/> asks the
/// window to show it (a preview of changes). When <see cref="Dismiss"/> is cancelled the window closes as if Cancel were chosen
/// (a question that stops mattering, such as "Keep the swapped buttons?" after its time ran out).
/// </summary>
public sealed record EngineMessage(string Title, string Text, string? Confirm = null, string? Details = null, bool ShowDetails = false,
    CancellationToken Dismiss = default);
