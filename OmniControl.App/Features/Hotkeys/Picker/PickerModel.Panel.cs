using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The panel mode of the picker (<see cref="IPanelMembership"/>): entries already in the panel are marked, can be taken out, and filtered to.
public sealed partial class PickerModel
{
    private const string AddAgainText = "Add again";

    private IPanelMembership? _panel;

    /// The panel the picker was opened from; null for every other picker.
    public IPanelMembership? Panel
    {
        get => _panel;
        init
        {
            _panel = value;
            if (value is not null) value.Changed += OnPanelChanged;
        }
    }

    public bool HasPanel => _panel is not null;

    /// "In panel only": the lists show only the entries that are in the panel.
    [ObservableProperty] private bool _onlyInPanel;

    /// The highlighted entry is in the panel: the settings pane's foot offers to take it out, and "Use this ..." reads "Add again".
    public bool ActiveInPanel => ActiveChoice is { } choice && IsInPanel(choice);

    public string PanelNote => ActiveChoice is { } choice ? $"{choice.Name} is already in the panel" : string.Empty;

    partial void OnOnlyInPanelChanged(bool value) => Reload();

    private bool IsInPanel(PickChoice choice) => choice.Command is null && _panel?.Contains(choice.Spec) == true;

    /// "Remove from panel": the highlighted entry is taken out at once.
    public void RemoveFromPanel()
    {
        if (ActiveChoice is { } choice && IsInPanel(choice)) _panel!.Remove(choice.Spec);
    }

    private void OnPanelChanged()
    {
        RefreshKeys();
        NotifyPanelChanged();
    }

    private void NotifyPanelChanged()
    {
        OnPropertyChanged(nameof(ActiveInPanel));
        OnPropertyChanged(nameof(PanelNote));
        OnPropertyChanged(nameof(UseText));
    }
}
