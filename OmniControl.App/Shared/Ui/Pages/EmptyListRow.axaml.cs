using System.Windows.Input;

namespace OmniControl.App.Shared.Ui.Pages;

/// The empty-list row of a group: a dashed outline, one sentence, a link and, when the group has another way in, a second link; layout only.
public partial class EmptyListRow : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<EmptyListRow, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string> LinkTextProperty = AvaloniaProperty.Register<EmptyListRow, string>(nameof(LinkText), string.Empty);
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<EmptyListRow, ICommand?>(nameof(Command));

    public static readonly StyledProperty<string> SecondLinkTextProperty = AvaloniaProperty.Register<EmptyListRow, string>(nameof(SecondLinkText), string.Empty);
    public static readonly StyledProperty<ICommand?> SecondCommandProperty = AvaloniaProperty.Register<EmptyListRow, ICommand?>(nameof(SecondCommand));

    public EmptyListRow() => InitializeComponent();

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string LinkText { get => GetValue(LinkTextProperty); set => SetValue(LinkTextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public string SecondLinkText { get => GetValue(SecondLinkTextProperty); set => SetValue(SecondLinkTextProperty, value); }
    public ICommand? SecondCommand { get => GetValue(SecondCommandProperty); set => SetValue(SecondCommandProperty, value); }
}
