namespace OmniControl.App.Features.Volume.Pages.Easy;

/// A preset chip of the Sound drawer ("Bass boost"); the one for the user's own sound can be clicked only once there is one to bring back.
public sealed partial class EasyChip : ObservableObject
{
    public EasyChip(string name, Action pick)
    {
        Name = name;
        PickCommand = new RelayCommand(pick, () => CanPick);
    }

    public string Name { get; }

    public IRelayCommand PickCommand { get; }

    /// The chips of a row (Controls/ChoiceChips) may carry an icon; the Sound chips do not.
    public string? Icon => null;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private bool _canPick = true;

    partial void OnCanPickChanged(bool value) => PickCommand.NotifyCanExecuteChanged();
}
