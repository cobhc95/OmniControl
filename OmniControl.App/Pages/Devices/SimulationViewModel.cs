using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;
using OmniControl.App.Dialogs;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// The simulation method of one device on the Devices page (docs/mockups/devices-simulation.html): the emulator and its adb address, and the
/// recorded steps as an editable list. It shows and edits; what the steps do is Core's (<see cref="SimRunner"/>) and what runs and when is
/// <see cref="DeviceService"/>'s. While steps are being recorded or an element is being picked again the device counts as busy.
/// </summary>
public sealed partial class SimulationViewModel : ObservableObject, IDisposable
{
    private readonly DeviceRecord _record;
    private readonly DeviceService _service;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string> _report;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action _changed;
    private SimRecorder? _recorder;
    private CancellationTokenSource? _picking;

    public SimulationViewModel(DeviceRecord record, DeviceService service, Action<string> report, Func<DialogMessage, Task<bool>> ask, Action changed)
    {
        _record = record;
        _service = service;
        _runtime = service.RuntimeOf(record);
        _report = report;
        _ask = ask;
        _changed = changed;
        _adbAddress = record.AdbAddress;
        AddOptions = SimSteps.All.Select(kind => new MenuRowItem(SimSteps.NameOf(kind), string.Empty, false, string.Empty, new RelayCommand(() => AddStep(kind)))).ToList();
        foreach (var step in record.Steps) Rows.Add(MakeRow(step));
        Renumber();
        Refresh();
    }

    public ObservableCollection<SimStepItem> Rows { get; } = new();

    /// <summary>The emulator menu: the installed ones to pick, the others with "Get it".</summary>
    public IReadOnlyList<MenuRowItem> EmulatorOptions { get; private set; } = Array.Empty<MenuRowItem>();

    /// <summary>The "+ add step" menu.</summary>
    public IReadOnlyList<MenuRowItem> AddOptions { get; }

    [ObservableProperty] private string _adbAddress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RecordLabel))] private bool _isRecording;

    /// <summary>One line of guidance while recording or picking, empty otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasHelp))] private string _help = string.Empty;

    public bool HasHelp => Help.Length > 0;
    public string RecordLabel => IsRecording ? "Stop" : "Record";
    public bool HasSteps => Rows.Count > 0;

    public string EmulatorText => $"Emulator: {_service.Simulation.EmulatorOf(_record)?.Name ?? "none found"}";
    public string EmulatorTip => _service.Simulation.EmulatorOf(_record)?.AdbNote ?? "Pick an emulator, or get one with the menu.";
    public string AddressWatermark => _service.Simulation.EmulatorOf(_record)?.DefaultAddress is { Length: > 0 } usual ? $"adb address (usually {usual})" : "adb address";
    public bool IsConnected => _service.Simulation.IsConnected(_service.Simulation.AddressOf(_record));
    public bool CanOpenEmulator => _service.Simulation.EmulatorOf(_record) is { } emulator && _service.Simulation.IsInstalled(emulator);
    public bool CanConnect => _service.Simulation.AdbPath is not null && _service.Simulation.AddressOf(_record).Length > 0;
    public bool CanWork => !_runtime.Busy || IsRecording || _picking is not null;

    /// <summary>Reads what depends on the PC and the device again (the emulator choice, the connection, the step notes).</summary>
    public void Refresh()
    {
        var chosen = _service.Simulation.EmulatorOf(_record);
        EmulatorOptions = Emulators.All.Select(e => _service.Simulation.IsInstalled(e)
            ? new MenuRowItem(e.Name, string.Empty, e == chosen, e.AdbNote, new RelayCommand(() => Choose(e)))
            : new MenuRowItem(e.Name, "Get it", false, $"Opens {e.Name}'s official download page in your browser.", new RelayCommand(() => GetEmulator(e)))).ToList();
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].RunFailure = _runtime.FailedStep == i ? _runtime.FailedReason : string.Empty;
            Rows[i].Notify();
        }
        foreach (var name in new[]
                 {
                     nameof(EmulatorOptions), nameof(EmulatorText), nameof(EmulatorTip), nameof(AddressWatermark), nameof(IsConnected), nameof(CanOpenEmulator),
                     nameof(CanConnect), nameof(CanWork), nameof(HasSteps),
                 })
            OnPropertyChanged(name);
        RecordCommand.NotifyCanExecuteChanged();
        TestAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnAdbAddressChanged(string value)
    {
        _record.AdbAddress = value.Trim();
        Save();
    }

    private void Choose(EmulatorInfo emulator)
    {
        _record.EmulatorId = emulator.Id;
        Save();
    }

    private void GetEmulator(EmulatorInfo emulator)
    {
        if (_service.Simulation.OpenDownloadPage(emulator) is { } problem) _report(problem);
    }

    [RelayCommand]
    private void OpenEmulator()
    {
        if (_service.Simulation.EmulatorOf(_record) is { } emulator && _service.Simulation.Open(emulator) is { } problem) _report(problem);
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (await _service.Simulation.ConnectAsync(_service.Simulation.AddressOf(_record)) is { } problem) _report($"{problem} Details are in {DeviceService.LogFile}.");
    }

    // ---- The steps ----

    private SimStepItem MakeRow(SimStep step) => new(step, _ => Save(), Remove, TestStepAsync, RePickAsync);

    private void Save()
    {
        _service.Edited();
        _changed();
    }

    private void Renumber()
    {
        for (var i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
        OnPropertyChanged(nameof(HasSteps));
    }

    /// <summary>A new step goes before the closing Check (which stays last), or at the end.</summary>
    public void AddStep(SimStepKind kind)
    {
        var step = SimSteps.Blank(kind);
        var at = kind != SimStepKind.Check && Rows.Count > 0 && Rows[^1].Step.Kind == SimStepKind.Check ? Rows.Count - 1 : Rows.Count;
        _record.Steps.Insert(at, step);
        Rows.Insert(at, MakeRow(step));
        Renumber();
        Save();
    }

    private void Remove(SimStepItem row)
    {
        _record.Steps.Remove(row.Step);
        Rows.Remove(row);
        Renumber();
        Save();
    }

    /// <summary>Dragged rows (ascending positions) go, in their order, in front of the <paramref name="insertAt"/>-th of the rows that stay.</summary>
    public void MoveRows(IReadOnlyList<int> moved, int insertAt)
    {
        var moving = moved.Select(i => Rows[i]).ToList();
        var order = Rows.Except(moving).ToList();
        order.InsertRange(Math.Clamp(insertAt, 0, order.Count), moving);
        Rows.Clear();
        _record.Steps.Clear();
        foreach (var row in order)
        {
            Rows.Add(row);
            _record.Steps.Add(row.Step);
        }
        Renumber();
        Save();
    }

    private async Task TestStepAsync(SimStepItem row)
    {
        var problem = await _service.TestStepAsync(_record, row.Step, Rows.IndexOf(row));
        row.TestFailure = problem ?? string.Empty;
        row.Notify();
        if (problem is not null) _report($"{problem} Details are in {DeviceService.LogFile}.");
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task TestAll()
    {
        if (await _service.TestAsync(_record) is { } problem) _report($"{problem} Details are in {DeviceService.LogFile}.");
        _changed();
    }

    // ---- Recording and picking again ----

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task Record()
    {
        if (IsRecording) { StopRecording(); return; }
        if (Rows.Count > 0 && !await _ask(new DialogMessage("Record again?", "The steps of this device will be replaced by the new recording.", "Record again"))) return;
        var (recorder, problem) = await _service.StartRecorderAsync(_record);
        if (recorder is null) { _report(problem!); return; }
        _recorder = recorder;
        _record.Steps.Clear();
        Rows.Clear();
        if (await recorder.CurrentPackageAsync() is { Length: > 0 } package)
        {
            var open = SimSteps.Blank(SimStepKind.OpenApp);
            open.Package = package;
            _record.Steps.Add(open);
            Rows.Add(MakeRow(open));
        }
        recorder.Recorded += OnRecorded;
        recorder.BeginRecording();
        IsRecording = true;
        _runtime.Busy = true;
        Help = "Recording: tap through the app in the emulator and switch the device on, then press Stop.";
        Renumber();
        Save();
    }

    private void OnRecorded(SimStep step) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsRecording) return;
        _record.Steps.Add(step);
        Rows.Add(MakeRow(step));
        Renumber();
        Save();
    });

    private void StopRecording()
    {
        _recorder?.EndRecording();
        Release();
        IsRecording = false;
        if (Rows.Count > 0 && Rows[^1].Step.Kind != SimStepKind.Check) AddStep(SimStepKind.Check);
        Help = Rows.Count > 0 ? "Now use Re-pick on the last step and tap the words in the emulator that show while the device is on." : string.Empty;
        _changed();
    }

    private async Task RePickAsync(SimStepItem row)
    {
        if (row.IsPicking) { _picking?.Cancel(); return; }
        if (IsRecording || _picking is not null) { _report("Finish the recording or the other pick first."); return; }
        var (recorder, problem) = await _service.StartRecorderAsync(_record);
        if (recorder is null) { _report(problem!); return; }
        _recorder = recorder;
        _picking = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
        row.IsPicking = true;
        _runtime.Busy = true;
        Help = "Tap the element in the emulator. Pick again to cancel.";
        _changed();
        try
        {
            if (await recorder.PickAsync(_picking.Token) is { } pick) Apply(row, pick.Touch, pick.Screen);
        }
        finally
        {
            row.IsPicking = false;
            _picking.Dispose();
            _picking = null;
            Release();
            Help = string.Empty;
            _changed();
        }
    }

    /// <summary>The element under the touch becomes the step's: a Check takes the words there, the others the element's text, description and id.</summary>
    private void Apply(SimStepItem row, TouchGesture touch, SimScreen? screen)
    {
        var step = row.Step;
        if (step.Kind == SimStepKind.Check)
        {
            var words = screen?.WordsAt(touch.ToX, touch.ToY) ?? string.Empty;
            if (words.Length == 0) { _report("That spot shows no words. Tap the words that show while the device is on."); return; }
            step.Text = words;
            step.Description = string.Empty;
        }
        else
        {
            var tapped = screen?.TapAt(touch.ToX, touch.ToY) ?? SimRecorder.StepOf(touch, null);
            (step.Text, step.Description, step.ResourceId, step.X, step.Y) = (tapped.Text, tapped.Description, tapped.ResourceId, tapped.X, tapped.Y);
        }
        row.Picked();
    }

    private void Release()
    {
        if (_recorder is { } recorder)
        {
            recorder.Recorded -= OnRecorded;
            recorder.Dispose();
        }
        _recorder = null;
        _runtime.Busy = false;
    }

    public void Dispose()
    {
        _picking?.Cancel();
        if (_recorder is not null) Release();
    }
}
