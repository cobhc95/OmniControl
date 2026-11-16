namespace OmniControl.App.Dialogs;

/// <summary>The one way a view-model asks the user a short question or shows a short message: a small window above the main window.</summary>
public sealed class DialogService
{
    /// <summary>Shows <paramref name="message"/>; true when the user confirmed (its confirm button), false for Close, Cancel or Esc.</summary>
    public async Task<bool> AskAsync(DialogMessage message) => await ChooseAsync(message) == DialogAnswer.Confirm;

    /// <summary>Shows <paramref name="message"/> with its two answers; which one the user chose, or Cancel.</summary>
    public Task<DialogAnswer> ChooseAsync(DialogMessage message) => new MessageWindow(message).AskAsync();
}
