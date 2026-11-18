using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages.Osd;

/// <summary>
/// Wiring only: shows an <see cref="OsdDurationViewModel"/>; the exception rows are reordered by dragging (<see cref="ListDragReorder"/>), each
/// chip's menu is filled with its row's choices when it opens and closes after a pick; Esc (the dialog style) and Close shut it.
/// </summary>
public partial class OsdDurationWindow : Window
{
    public OsdDurationWindow()
    {
        InitializeComponent();
        ListDragReorder.Attach(ExceptionRows, (rows, at) => (DataContext as OsdDurationViewModel)?.Move(rows, at));
    }

    private void OnChipMenuOpened(object? sender, Flyout flyout)
    {
        if (sender is not ChipMenu { DataContext: OsdDurationRowItem row } chip) return;
        var list = new MenuRowList { ItemsSource = row.MenuRows };
        list.Picked += (_, _) => flyout.Hide();
        chip.Menu = list;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
