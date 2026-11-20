namespace OmniControl.App.Shared.Ui.Controls;

/// One pill of a <see cref="ChoiceChips"/> row made in code (Other PCs' actions): its words, an optional icon id, what it does, whether it shows as the one on now (accent), an optional muted note and how far what it started has come.
public sealed partial class ChipItem : ObservableObject
{
    public ChipItem(string name, IRelayCommand pickCommand, string? icon) => (_name, PickCommand, _icon) = (name, pickCommand, icon);

    /// The words (the tooltip of an icon-only pill); may change while shown, as the Now playing pill's Pause / Play does.
    [ObservableProperty] private string _name;

    [ObservableProperty] private string? _icon;

    public IRelayCommand PickCommand { get; }

    /// What the owner calls it (an action's id); the view does not show it.
    public string Key { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;

    /// Small muted words after the name (a power action's "in 30 s"); null: none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string? _note;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// How far what the pill started has come (0 to 1): the pill is accent-outlined and fills from the left; null: it is a plain pill.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCounting))] private double? _progress;

    public bool IsCounting => Progress is not null;
}
