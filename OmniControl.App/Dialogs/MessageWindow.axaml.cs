using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;

namespace OmniControl.App.Dialogs;

/// <summary>Wiring only: shows an <see cref="DialogMessage"/> and closes with Confirm, Other (its second answer) or Cancel (Close/Cancel, Esc, the window's X, or the message's Dismiss).</summary>
public partial class MessageWindow : Window
{
    private readonly string? _details;
    private DialogAnswer _answer;

    public MessageWindow() => InitializeComponent();

    public MessageWindow(DialogMessage message) : this()
    {
        Title = message.Title;
        Heading.Text = message.Title;
        Message.Text = message.Text;
        _details = message.Details;
        DetailsBox.Text = message.Details;
        DetailsBox.IsVisible = message.ShowDetails && message.Details is not null;
        CopyButton.IsVisible = message.Details is not null;
        ConfirmButton.Content = message.Confirm;
        ConfirmButton.IsVisible = message.Confirm is not null;
        OtherButton.Content = message.Other;
        OtherButton.IsVisible = message.Other is not null;
        CloseButton.Content = message.Confirm is null ? "Close" : "Cancel";
        Opened += (_, _) => (message.Confirm is null ? CloseButton : ConfirmButton).Focus();
        if (message.Dismiss.CanBeCanceled)
        {
            var dismissed = message.Dismiss.Register(() => Dispatcher.UIThread.Post(Close));
            Closed += (_, _) => dismissed.Dispose();
        }
    }

    /// <summary>Shows the window (kept above <paramref name="owner"/>, the main window when null) and waits until it closes.</summary>
    public Task<DialogAnswer> AskAsync(Window? owner = null)
    {
        var done = new TaskCompletionSource<DialogAnswer>();
        Closed += (_, _) => done.TrySetResult(_answer);
        OwnedWindow.Show(this, owner);
        return done.Task;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_details is null || Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(_details);
        }
        catch (Exception ex) when (ex is TimeoutException or ExternalException)
        {
            // Another program holds the clipboard: the details are still in the setup log, whose path the message names.
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _answer = DialogAnswer.Confirm;
        Close();
    }

    private void OnOtherClick(object? sender, RoutedEventArgs e)
    {
        _answer = DialogAnswer.Other;
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
