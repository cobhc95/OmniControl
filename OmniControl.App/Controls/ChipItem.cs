using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Controls;

/// <summary>One pill of a <see cref="ChoiceChips"/> row made in code (Other PCs' actions): its words, an optional icon id, what it does, and whether it shows as the one on now (accent).</summary>
public sealed partial class ChipItem : ObservableObject
{
    public ChipItem(string name, IRelayCommand pickCommand, string? icon) => (_name, PickCommand, _icon) = (name, pickCommand, icon);

    /// <summary>The words (the tooltip of an icon-only pill); may change while shown, as the Now playing pill's Pause / Play does.</summary>
    [ObservableProperty] private string _name;

    [ObservableProperty] private string? _icon;

    public IRelayCommand PickCommand { get; }

    /// <summary>What the owner calls it (an action's id); the view does not show it.</summary>
    public string Key { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
}
