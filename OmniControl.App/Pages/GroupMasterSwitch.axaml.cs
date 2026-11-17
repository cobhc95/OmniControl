using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages;

/// <summary>The master switch of a group header: shows the group's all-on / mixed / all-off state live and hands a click to <see cref="CardGroup.ToggleAll"/>.</summary>
public partial class GroupMasterSwitch : UserControl
{
    private CardGroup? _group;

    public GroupMasterSwitch()
    {
        InitializeComponent();
        Switch.Click += OnClick;
        DataContextChanged += (_, _) => Follow(DataContext as CardGroup);
        DetachedFromVisualTree += (_, _) => Follow(null);
        AttachedToVisualTree += (_, _) => Follow(DataContext as CardGroup);
    }

    private void Follow(CardGroup? group)
    {
        if (ReferenceEquals(group, _group)) { Show(); return; }
        if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
        _group = group;
        if (_group is not null) _group.PropertyChanged += OnGroupChanged;
        Show();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardGroup.MasterState)) Show();
    }

    /// <summary>The click already flipped the switch on its own; the group decides what the new state really is and the switch is set to that.</summary>
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        _group?.ToggleAll();
        Show();
    }

    private void Show()
    {
        var state = _group?.MasterState;
        Switch.IsChecked = state;
        Dash.IsVisible = _group?.HasMaster == true && state is null;
    }
}
