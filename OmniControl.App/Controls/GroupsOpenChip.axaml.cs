using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>Wiring only: the chip shows the "Groups when a page opens" choice and its menu sets it (<see cref="GroupsOpen"/> owns the setting).</summary>
public partial class GroupsOpenChip : UserControl
{
    private Flyout? _flyout;

    public GroupsOpenChip()
    {
        InitializeComponent();
        Chip.Text = GroupsOpen.ChipText;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        GroupsOpen.Changed += Refresh;
        Refresh();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        GroupsOpen.Changed -= Refresh;
        base.OnUnloaded(e);
    }

    private void Refresh() => Chip.Text = GroupsOpen.ChipText;

    private void OnOpened(object? sender, Flyout flyout)
    {
        _flyout = flyout;
        Rows.ItemsSource = GroupsOpen.All
            .Select(o => new MenuRowItem(o.Label, string.Empty, o.Value == GroupsOpen.Current, string.Empty, new RelayCommand(() => GroupsOpen.Set(o.Value))))
            .ToList();
    }

    private void OnClosed(object? sender, EventArgs e) => _flyout = null;

    private void OnPicked(object? sender, EventArgs e) => _flyout?.Hide();
}
