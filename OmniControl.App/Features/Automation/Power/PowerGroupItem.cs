namespace OmniControl.App.Features.Automation.Power;

/// One row of the plan editor: a setting's name and its two values.
public sealed record PowerSettingRow(string Name, PowerValueCell PluggedIn, PowerValueCell OnBattery);

/// A collapsible group of settings as Windows lists them; closed, its settings' names are shown in muted words.
public sealed partial class PowerGroupItem : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary), nameof(IsClosed))] private bool _isOpen;

    public PowerGroupItem(string name, IReadOnlyList<PowerSettingRow> rows, bool open)
    {
        (Name, Rows, _isOpen) = (name, rows, open);
    }

    public string Name { get; }
    public IReadOnlyList<PowerSettingRow> Rows { get; }
    public bool IsClosed => !IsOpen;

    /// The names of the group's settings while it is closed.
    public string Summary => IsOpen ? string.Empty : string.Join(" · ", Rows.Select(r => r.Name));

    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
}
