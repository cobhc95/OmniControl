using Avalonia.Controls.Documents;
using Avalonia.Input;

namespace OmniControl.App.Shared.Dialogs;

/// Wiring only: shows an <see cref="DialogMessage"/> and closes with Confirm, Other (its second answer) or Cancel (Close/Cancel, Esc, the window's X, or the message's Dismiss).
public partial class MessageWindow : Window
{
    private readonly string? _details;
    private DialogAnswer _answer;

    /// The box was ticked (only a window that offers it can have it ticked).
    public bool DontAskAgain => DontAsk.IsVisible && DontAsk.IsChecked == true;

    /// What was typed in the box of a question that asks for text, trimmed.
    public string Typed => (InputBox.Text ?? string.Empty).Trim();

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
        DontAsk.IsVisible = message.CanRemember;
        ConfirmButton.Content = message.Confirm;
        ConfirmButton.IsVisible = message.Confirm is not null;
        OtherButton.Content = message.Other;
        OtherButton.IsVisible = message.Other is not null;
        CloseButton.Content = message.Cancel ?? (message.Confirm is null ? "Close" : "Cancel");
        ConfirmButton.Classes.Set("danger", message.Danger);
        if (message.HasAccessKeys) UseAccessKeys(message);
        InputBox.IsVisible = message.AsksText;
        InputBox.Text = message.Input;
        Message.Classes.Set("hint", message.AsksText);
        Opened += (_, _) =>
        {
            if (message.HasAccessKeys) NoButton.Focus(); // Enter must not undo something by accident
            else if (!message.AsksText) (message.Confirm is null ? CloseButton : ConfirmButton).Focus();
            else
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
        };
        if (message.Dismiss.CanBeCanceled)
        {
            var dismissed = message.Dismiss.Register(() => Dispatcher.UIThread.Post(Close));
            Closed += (_, _) => dismissed.Dispose();
        }
    }

    /// Shows the window (kept above <paramref name="owner"/>, the main window when null) and waits until it closes.
    public Task<DialogAnswer> AskAsync(Window? owner = null)
    {
        var done = new TaskCompletionSource<DialogAnswer>();
        Closed += (_, _) => done.TrySetResult(_answer);
        OwnedWindow.Show(this, owner);
        return done.Task;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, _details);

    // A Yes/No question: Cancel (with its Esc hint) first, then No and the confirm button, the first letter of each underlined; Y and N answer from anywhere in the window (Esc is the dialog window's own).
    private void UseAccessKeys(DialogMessage message)
    {
        if (CloseButton.Parent is Panel foot)
        {
            foot.Children.Remove(CloseButton);
            foot.Children.Insert(0, CloseButton);
        }
        CloseButton.Classes.Set("primary", false);
        CloseButton.Classes.Add("usebtn");
        CloseButton.Classes.Add("scope");
        CloseButton.Tag = "scope";
        var cancel = new TextBlock { Inlines = new() { new Run("Cancel") } };
        var esc = new Run("  Esc");
        if (this.TryFindResource("BrushMuted", ActualThemeVariant, out var muted) && muted is IBrush brush) esc.Foreground = brush;
        cancel.Inlines!.Add(esc);
        CloseButton.Content = cancel;
        NoButton.IsVisible = true;
        NoButton.Content = Keyed("No");
        ConfirmButton.Content = Keyed(message.Confirm!);
        KeyDown += OnAccessKey;
        Closed += (_, _) => KeyDown -= OnAccessKey;
    }

    private static TextBlock Keyed(string text) => new()
    {
        Inlines = new() { new Run(text[..1]) { TextDecorations = TextDecorations.Underline }, new Run(text[1..]) },
    };

    private void OnAccessKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Source is TextBox) return;
        if (e.Key == Key.Y) OnConfirmClick(sender, e);
        else if (e.Key == Key.N) OnCloseClick(sender, e);
        else return;
        e.Handled = true;
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnConfirmClick(sender, e);
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
