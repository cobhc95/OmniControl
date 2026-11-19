namespace OmniControl.App.Shared.Dialogs;

/// The one way a view-model asks the user a short question or shows a short message: a small window above the main window.
public sealed class DialogService
{
    public DialogService(ConfirmationMemory? confirmations = null) => Confirmations = confirmations ?? new ConfirmationMemory();

    /// The prompts the user ticked "Don't ask again" on.
    public ConfirmationMemory Confirmations { get; }

    /// Shows <paramref name="message"/>; true when the user confirmed (its confirm button), false for Close, Cancel or Esc.
    public async Task<bool> AskAsync(DialogMessage message) => await ChooseAsync(message) == DialogAnswer.Confirm;

    /// Asks for one line of text (<paramref name="message"/> with its <see cref="DialogMessage.Input"/> set); the trimmed text, or null when the user cancelled.
    public async Task<string?> AskTextAsync(DialogMessage message)
    {
        var window = new MessageWindow(message);
        return await window.AskAsync() == DialogAnswer.Confirm ? window.Typed : null;
    }

    /// Shows <paramref name="message"/> with its two answers; which one the user chose, or Cancel.
    public async Task<DialogAnswer> ChooseAsync(DialogMessage message)
    {
        if (Confirmations.Skips(message)) return DialogAnswer.Confirm;
        var window = new MessageWindow(message);
        var answer = await window.AskAsync();
        Confirmations.Settle(message, answer, window.DontAskAgain);
        return answer;
    }
}
