using OmniControl.App.Features.Devices;

namespace OmniControl.App.Features.Devices.Pages;

/// One method of a device on the Smart Devices page: its row in the device's method list
/// (number, "Scripts · Tapo", ready or set-up, how fast and where it works) and its own set-up (the numbered stages, one open at a time,
/// and the Test stage). The method's own fields are <c>MethodItem.Scripts.cs</c> and <c>MethodItem.Simulation.cs</c>. What the device is
/// told and how is <see cref="DeviceService"/>'s; this only shows and edits it.
public sealed partial class MethodItem : ObservableObject, IDisposable
{
    private readonly DeviceService _service;
    private readonly DeviceRecord _device;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string> _report;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action _changed;
    private readonly Action<MethodItem> _edit;

    public MethodRecord Record { get; }

    /// The simulation method's emulator, adb address, recorded steps and teaching.
    public SimulationViewModel Simulation { get; }

    /// <param name="changed">Something the device shows changed (the device item refreshes itself and every method).</param>
    /// <param name="edit">Opens this method's set-up (the row, its "Edit setup…" link, Re-teach).</param>
    public MethodItem(DeviceRecord device, MethodRecord record, DeviceService service, Action<string> report, Func<DialogMessage, Task<bool>> ask, Action changed, Action<MethodItem> edit)
    {
        _device = device;
        Record = record;
        _service = service;
        _runtime = service.RuntimeOf(device);
        _report = report;
        _ask = ask;
        _changed = changed;
        _edit = edit;
        _address = record.Address;
        _email = record.Email;
        _reserved = record.Reserved;
        Simulation = new SimulationViewModel(device, record, service, report, ask, changed, AdvanceSetup);
        _scriptStages = new SetupStages(MakeScriptStages);
        _simulationStages = new SetupStages(MakeSimulationStages);
        LoadDraft();
    }

    public void Dispose() => Simulation.Dispose();

    public bool IsSimulation => Record.IsSimulation;
    public bool IsScripts => !IsSimulation;

    public string MethodLabel => $"Method: {Record.Kind}";
    public string MethodTip => DeviceMethods.Tip(Record.Kind);

    /// The Method menu: every built method of the master list with its tags and brief, a tick on this one's kind.
    public IReadOnlyList<MenuRowItem> MethodOptions =>
        DeviceMethods.All.Select(m => new MenuRowItem(m.Name, string.Empty, m.Name == Record.Kind, m.Tip, new RelayCommand(() => PickMethod(m.Name)), m.Tags, m.Brief)).ToList();

    private void PickMethod(string kind)
    {
        if (Record.Kind == kind) return;
        _service.ChangeKind(_device, Record, kind);
        if (IsSimulation) _ = _service.Simulation.RefreshAsync();
        _changed();
        Stages.OpenNext();
    }

    // ---- The row in the device's method list ----

    /// Its place in the device's order (1 = tried first).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NumberText), nameof(RowNote))] private int _number = 1;

    public string NumberText => Number.ToString();

    /// "Scripts · Tapo" or "Simulation · Android Studio".
    public string RowTitle => $"{_device.NameOf(Record)} · {(IsSimulation ? _service.Simulation.EmulatorOf(Record)?.Name ?? "no emulator" : Profile.Name)}";

    /// How fast and where it works ("~1 s · home Wi-Fi"); a method after the first is the "fallback".
    public string RowNote
    {
        get
        {
            var method = DeviceMethods.Of(Record.Kind);
            return $"{(Number > 1 ? "fallback" : method.Speed)} · {method.Where}";
        }
    }

    /// What the method does; a simulation also says which emulator, whether it runs hidden and how long it stays up.
    public string RowTip
    {
        get
        {
            if (IsScripts) return MethodTip;
            var emulator = _service.Simulation.EmulatorOf(Record);
            var runs = emulator is { CanHide: false } ? "shows its window" : "runs hidden";
            return $"{MethodTip} {string.Join(" · ", new[] { emulator?.Name ?? "no emulator", runs, Simulation.KeepWords })}";
        }
    }

    public bool IsReady => Record.IsReady;

    // ---- The row's power button ----

    /// The device is on, as the master list of states says (the button's accent look).
    public bool PowerOn => _service.PowerOf(_device) == true;

    /// This method is the one running now (the button shows its spinner).
    public bool PowerRunning => _runtime.Busy && _runtime.RunningMethodId == Record.Id;

    /// This method's last try failed (the button shows the red warning; the reason is its tooltip).
    public bool PowerFailed => !PowerRunning && _runtime.ProblemOf(Record.Id) is not null;

    public bool PowerNormal => !PowerRunning && !PowerFailed;

    public string PowerTip => PowerFailed ? $"Failed: {_runtime.ProblemOf(Record.Id)}"
        : PowerRunning ? "Stop" : $"Switch the device through {_device.NameOf(Record)} only (no other method is tried).";

    /// Switches the device through this method only (the result goes to the device's "Last command" line); while this method runs, stops it.
    [RelayCommand(CanExecute = nameof(CanPower))]
    private void Power()
    {
        if (PowerRunning) _runtime.Stop();
        else _ = _service.RunAsync(_device, DevicePower.Toggle, Record);
    }

    private bool CanPower() => !_runtime.Busy || PowerRunning;

    /// "Edit setup…" on the row (a click on the row does the same).
    [RelayCommand]
    private void EditSetup() => _edit(this);

    /// The recent readings were unsure (the app probably changed): one plain sentence and Re-teach on the device's Ready view.
    public bool HasDrift => IsSimulation && IsReady && (Record.Taught.IsDrifting || (_runtime.Unsure && _runtime.FailedMethodId == Record.Id));

    public string DriftText => _runtime.Unsure && _runtime.FailedMethodId == Record.Id
        ? SimulationMethod.ReTeachSentence
        : "OmniControl is less sure of its state lately; the app may have changed. Teach on and off again.";

    /// Reads everything this method shows again (the device item calls it on every refresh).
    public void Refresh()
    {
        Stages.Refresh();
        Simulation.Refresh();
        RefreshScripts();
        foreach (var property in new[]
                 {
                     nameof(Stages), nameof(MethodLabel), nameof(MethodTip), nameof(MethodOptions), nameof(IsSimulation), nameof(IsScripts), nameof(IsReady), nameof(CanFinish),
                     nameof(RowTitle), nameof(RowNote), nameof(RowTip), nameof(HasDrift), nameof(DriftText), nameof(NeedsAdb),
                     nameof(PowerOn), nameof(PowerRunning), nameof(PowerFailed), nameof(PowerNormal), nameof(PowerTip),
                 })
            OnPropertyChanged(property);
        PowerCommand.NotifyCanExecuteChanged();
    }

    private void Changed() => _changed();
}
