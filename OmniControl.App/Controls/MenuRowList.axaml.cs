using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Controls;

/// <summary>One row of a <see cref="MenuRowList"/>: its words, a tag at the right (may be empty), whether it is the one that is on now, a tooltip and what it does.</summary>
public sealed record MenuRowItem(string Text, string Note, bool IsOn, string Tip, ICommand Command)
{
    public bool HasNote => Note.Length > 0;
}

/// <summary>Layout and wiring only: the rows of a small menu; any row click raises <see cref="Picked"/> so the owner can close its pop-down (the row's own command runs as well).</summary>
public partial class MenuRowList : UserControl
{
    public MenuRowList() => InitializeComponent();

    public IEnumerable<MenuRowItem>? ItemsSource { get => Rows.ItemsSource as IEnumerable<MenuRowItem>; set => Rows.ItemsSource = value; }

    public event EventHandler? Picked;

    private void OnRowClick(object? sender, RoutedEventArgs e) => Picked?.Invoke(this, EventArgs.Empty);
}
