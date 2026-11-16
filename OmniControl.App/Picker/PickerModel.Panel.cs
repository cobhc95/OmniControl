using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;

namespace OmniControl.App.Picker;

/// <summary>The panel mode of the picker (<see cref="IPanelMembership"/>): entries already in the panel are marked, can be taken out, and filtered to.</summary>
public sealed partial class PickerModel
{
    private const string AddAgainText = "Add again";

    private IPanelMembership? _panel;

    /// <summary>The panel the picker was opened from; null for every other picker.</summary>
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

    /// <summary>"In panel only": the lists show only the entries that are in the panel.</summary>
    [ObservableProperty] private bool _onlyInPanel;

    /// <summary>The highlighted entry is in the panel: the settings pane's foot offers to take it out, and "Use this ..." reads "Add again".</summary>
    public bool ActiveInPanel => ActiveChoice is { } choice && IsInPanel(choice);

    public string PanelNote => ActiveChoice is { } choice ? $"{choice.Name} is already in the panel" : string.Empty;

    partial void OnOnlyInPanelChanged(bool value) => Reload();

    private bool IsInPanel(PickChoice choice) => choice.Command is null && _panel?.Contains(choice.Spec) == true;

    /// <summary>"Remove from panel": the highlighted entry is taken out at once.</summary>
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
