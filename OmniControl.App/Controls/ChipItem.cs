using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Controls;

/// <summary>One pill of a <see cref="ChoiceChips"/> row made in code (Other PCs' actions): its words, an optional icon id, what it does, and whether it shows as the one on now (accent).</summary>
public sealed partial class ChipItem : ObservableObject
{
    public ChipItem(string name, IRelayCommand pickCommand, string? icon) => (Name, PickCommand, Icon) = (name, pickCommand, icon);

    public string Name { get; }
    public IRelayCommand PickCommand { get; }
    public string? Icon { get; }

    /// <summary>What the owner calls it (an action's id); the view does not show it.</summary>
    public string Key { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
}
