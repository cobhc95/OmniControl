using System.ComponentModel;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Automation.Power;

/// The editor of one power plan in an action's settings pane: the plan's settings as Windows lists them, read live from Windows (nothing is
/// stored here), each change written to Windows at once. A failure shows one calm line and is logged.
public sealed partial class PowerPlanViewModel : ObservableObject, ISettingsPanel
{
    private const string ReadingWords = "Reading the plan from Windows…";
    private const string ShownWords = "Plan settings · apply at once";

    private readonly IPowerPlans _plans;
    private readonly Guid _plan;
    private List<PowerValueCell> _cells = new();

    [ObservableProperty] private string _status = ReadingWords;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem))] private string _problem = string.Empty;

    public PowerPlanViewModel(IPowerPlans plans, Guid plan)
    {
        (_plans, _plan) = (plans, plan);
        HasBattery = plans.HasBattery;
        _ = LoadAsync();
    }

    public ObservableCollection<PowerGroupItem> Groups { get; } = new();
    public bool HasBattery { get; }
    public bool CanRestore => _plans.CanRestoreDefaults(_plan);
    public bool HasProblem => Problem.Length > 0;

    public void CancelTyping()
    {
        foreach (var cell in _cells) cell.CancelTyping();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        Problem = string.Empty;
        if (Guard.Run("Restore power plan defaults", () => _plans.RestoreDefaults(_plan)) is not null) Problem = "Windows didn't restore the plan's defaults.";
        await LoadAsync();
    }

    /// Reads the plan's settings off the UI thread (a few hundred small calls) and shows them.
    private async Task LoadAsync()
    {
        Status = ReadingWords;
        try
        {
            var read = await Task.Run(() => _plans.Describe(_plan)
                .Select(g => (Group: g, Values: g.Settings.Select(s => _plans.Read(_plan, g.Id, s.Id)).ToList())).ToList());
            Show(read);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Guard.Log("Reading the power plan", ex);
            Status = "Windows didn't give this plan's settings.";
        }
    }

    private void Show(List<(PowerSubgroup Group, List<PowerValue> Values)> read)
    {
        _cells = new List<PowerValueCell>();
        Groups.Clear();
        foreach (var (group, values) in read)
        {
            var rows = group.Settings.Select((s, i) =>
            {
                var plugged = new PowerValueCell(s, values[i].PluggedIn, true, v => Write(group.Id, s, false, v));
                var battery = new PowerValueCell(s, values[i].OnBattery, HasBattery, v => Write(group.Id, s, true, v));
                _cells.Add(plugged);
                _cells.Add(battery);
                return new PowerSettingRow(s.Name, plugged, battery);
            }).ToList();
            Groups.Add(new PowerGroupItem(group.Name, rows, open: Groups.Count == 0));
        }
        Status = Groups.Count == 0 ? "This plan has no settings Windows lets you change." : ShownWords;
    }

    private bool Write(Guid group, PowerSetting setting, bool onBattery, uint value)
    {
        Problem = string.Empty;
        if (Guard.Run($"Writing power setting {setting.Name}", () => _plans.Write(_plan, group, setting.Id, onBattery, value)) is null) return true;
        Problem = "Windows didn't accept that change.";
        return false;
    }
}
