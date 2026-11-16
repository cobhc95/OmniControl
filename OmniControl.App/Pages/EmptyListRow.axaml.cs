using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages;

/// <summary>The empty-list row of a group: a dashed outline, one sentence and a link; layout only.</summary>
public partial class EmptyListRow : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<EmptyListRow, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string> LinkTextProperty = AvaloniaProperty.Register<EmptyListRow, string>(nameof(LinkText), string.Empty);
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<EmptyListRow, ICommand?>(nameof(Command));

    public EmptyListRow() => InitializeComponent();

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string LinkText { get => GetValue(LinkTextProperty); set => SetValue(LinkTextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
}
