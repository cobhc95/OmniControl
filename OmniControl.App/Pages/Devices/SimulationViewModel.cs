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
/// A simulation method of one device on the Devices page (docs/mockups/devices-simulation.html): the emulator and its adb address, and the
/// recorded steps as an editable list. It shows and edits; what the steps do is Core's (<see cref="SimRunner"/>) and what runs and when is
/// <see cref="DeviceService"/>'s. While steps are being recorded or an element is being picked again the device counts as busy.
/// </summary>
public sealed partial class SimulationViewModel : ObservableObject, IDisposable
{
    private readonly DeviceRecord _device;
    private readonly MethodRecord _record;
    private readonly DeviceService _service;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string> _report;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action _changed;
    private readonly Action _advance;
    private SimRecorder? _recorder;
    private readonly List<SimStep> _clipboard = new();
    private CancellationTokenSource? _picking;

    /// <param name="changed">Something the device item shows changed.</param>
    /// <param name="advance">A set-up stage's own action finished (the next stage may open).</param>
    public SimulationViewModel(DeviceRecord device, MethodRecord record, DeviceService service, Action<string> report, Func<DialogMessage, Task<bool>> ask, Action changed, Action advance)
    {
        _device = device;
        _record = record;
        _service = service;
        _runtime = service.RuntimeOf(device);
        _report = report;
        _ask = ask;
        _changed = changed;
        _advance = advance;
        _adbAddress = record.AdbAddress;
        AddOptions = SimSteps.All.Select(kind => new MenuRowItem(SimSteps.NameOf(kind), string.Empty, false, string.Empty, new RelayCommand(() => AddStep(kind)))).ToList();
        Reload();
    }

    /// <summary>Shows the record's steps and address again (the set-up's Cancel put them back).</summary>
    public void Reload()
    {
        Rows.Clear();
        foreach (var step in _record.Steps) Rows.Add(MakeRow(step));
        AdbAddress = _record.AdbAddress;
        Renumber();
        Refresh();
    }

    public ObservableCollection<SimStepItem> Rows { get; } = new();

    /// <summary>The emulator menu: the installed ones to pick, the others with "Get it".</summary>
    public IReadOnlyList<MenuRowItem> EmulatorOptions { get; private set; } = Array.Empty<MenuRowItem>();

    /// <summary>The "+ add step" menu.</summary>
    public IReadOnlyList<MenuRowItem> AddOptions { get; }

    [ObservableProperty] private string _adbAddress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RecordLabel), nameof(Help), nameof(HasHelp), nameof(RecordTip))] private bool _isRecording;

    /// <summary>The element being picked again says so here (empty otherwise).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Help), nameof(HasHelp))] private string _pickHelp = string.Empty;

    /// <summary>The thin status line while steps are being recorded.</summary>
    public const string RecordingLine = "Recording… tap your device's power button in the emulator.";

    /// <summary>One short sentence above the steps: what to do before recording, or while picking; empty otherwise.</summary>
    public string Help => IsRecording ? string.Empty : PickHelp.Length > 0 ? PickHelp : Rows.Count == 0 ? "Open the device's app in the emulator on the screen with your device, then press Start recording." : string.Empty;

    public bool HasHelp => Help.Length > 0;

    public string RecordLabel => IsRecording ? "Stop recording" : "Start recording";
    public string RecordTip => IsRecording ? "Ends the recording. The last tap is the device's switch." : "Records your taps in the emulator as steps. This replaces the steps.";
    public bool HasSteps => Rows.Count > 0;

    public string EmulatorText => $"Emulator: {_service.Simulation.EmulatorOf(_record)?.Name ?? "none found"}";
    public string EmulatorTip => _service.Simulation.EmulatorOf(_record)?.AdbNote ?? "Pick an emulator, or get one with the menu.";
    public string AddressWatermark => _service.Simulation.EmulatorOf(_record)?.DefaultAddress is { Length: > 0 } usual ? $"adb address (usually {usual})" : "adb address";
    public bool IsConnected => _service.Simulation.IsConnected(_service.Simulation.AddressOf(_record));
    public bool CanOpenEmulator => _service.Simulation.EmulatorOf(_record) is { } emulator && _service.Simulation.IsInstalled(emulator);
    public bool CanShow => CanOpenEmulator && !_working;
    public bool CanClose => CanOpenEmulator && IsConnected && !_working;

    private EmulatorKeeper Keeper => _service.Simulation.Keeper;

    /// <summary>The Keep running chip's words.</summary>
    public string KeepText => "Keep running: " + KeepChoice;

    /// <summary>The same choice as a phrase for the Ready view's line ("keep running until idle 30 min").</summary>
    public string KeepWords => Keeper.Settings.Keep == EmulatorKeep.AfterEachAction ? "closes after each action" : $"keep running {KeepChoice}";

    private string KeepChoice => Keeper.Settings.Keep switch
    {
        EmulatorKeep.Always => "always",
        EmulatorKeep.UntilIdle => $"until idle {IdleMinutes} min",
        _ => "close after each action",
    };

    public string KeepTip => "How long the emulator stays up after an action. An action starts it by itself when it is not running.";

    /// <summary>The state tag: running or stopped, hidden or shown (when OmniControl started it) and the memory it holds.</summary>
    public string StateText
    {
        get
        {
            if (!IsConnected) return "stopped";
            var parts = new List<string> { "running" };
            if (Keeper.Hidden is { } hidden) parts.Add(hidden ? "hidden" : "shown");
            if (MemoryText is { } memory) parts.Add(memory);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The memory the running emulator holds ("1.9 GB", from the gentle background look); null when it is not known.</summary>
    private string? MemoryText => _service.Simulation.EmulatorOf(_record) is { } emulator && _service.Simulation.MemoryOf(emulator) is > 0 and var bytes ? $"{bytes / 1073741824.0:0.0} GB" : null;

    // ---- The Simulation row's emulator chip (docs/mockups/emulator-chip.html) ----

    /// <summary>"Emulator: running · 1.9 GB" or "Emulator: closed".</summary>
    public string ChipText => IsConnected ? $"Emulator: running{(MemoryText is { } memory ? " · " + memory : string.Empty)}" : "Emulator: closed";

    public bool ChipRunning => IsConnected;

    public string ChipTip => IsConnected ? "Closes the emulator. Right-click for Show and how long it keeps running." : "Starts the emulator hidden. Right-click for Show and how long it keeps running.";

    /// <summary>The first rows of the chip's right-click menu: close (or start) now, and show the window; the Keep running menu follows them.</summary>
    public IReadOnlyList<MenuRowItem> EmulatorActions { get; private set; } = Array.Empty<MenuRowItem>();

    private void ReadEmulatorActions() => EmulatorActions = new[]
    {
        new MenuRowItem(IsConnected ? "Close now" : "Start", string.Empty, false, string.Empty, ChipCommand),
        new MenuRowItem("Show", string.Empty, false, "Shows the emulator's window, starting it first when it is closed.", ShowCommand),
    };

    /// <summary>A click on the chip: closes the running emulator, or starts the closed one hidden.</summary>
    [RelayCommand(CanExecute = nameof(CanChip))]
    private async Task Chip()
    {
        if (_service.Simulation.EmulatorOf(_record) is not { } emulator) return;
        var address = _service.Simulation.AddressOf(_record);
        await Working(() => IsConnected ? Keeper.CloseNowAsync(emulator, address) : Keeper.StartAsync(emulator, address));
    }

    private bool CanChip() => CanOpenEmulator && !_working;

    /// <summary>Minutes of no action before "until idle" closes the emulator (typed in the menu's number box).</summary>
    public double IdleMinutes
    {
        get => Keeper.Settings.IdleMinutes;
        set
        {
            Keeper.Settings.IdleMinutes = (int)value;
            Keeper.Edited();
            OnPropertyChanged();
        }
    }
    private bool _connecting;

    /// <summary>True while Show or Close now works (it can take a minute): the pills are off and the status line says what is happening.</summary>
    private bool _working;
    public bool CanConnect => !_connecting && !_working && _service.Simulation.AdbPath is not null &&_service.Simulation.AddressOf(_record).Length > 0;
    public bool CanWork => !_runtime.Busy || IsRecording || _picking is not null;

    /// <summary>Reads what depends on the PC and the device again (the emulator choice, the connection, the step notes).</summary>
    public void Refresh()
    {
        var chosen = _service.Simulation.EmulatorOf(_record);
        var setup = _service.Simulation;
        var installed = Emulators.All.Where(setup.IsInstalled).ToList();
        var missing = Emulators.All.Where(e => !setup.IsInstalled(e)).ToList();
        var options = new List<MenuRowItem>();
        if (installed.Count > 0) options.Add(MenuRowItem.Header("Installed"));
        options.AddRange(installed.Select(e => new MenuRowItem(e.Name, string.Empty, e == chosen, e.AdbNote, new RelayCommand(() => Choose(e)),
            new[] { EaseTag(e), new TagItem($"uses {e.UsesText}"), new TagItem(e.PcText) }, e.Summary, TagsOnOwnLine: true)));
        if (missing.Count > 0) options.Add(MenuRowItem.Header("Not installed", ruleAbove: installed.Count > 0));
        options.AddRange(missing.Select(e => new MenuRowItem(e.Name, "Get it ↗", false, $"Opens {e.Name}'s official download page in your browser.", new RelayCommand(() => GetEmulator(e)),
            Detail: $"{e.UsesText} · {e.PcText} · {char.ToLowerInvariant(e.Summary[0])}{e.Summary[1..].TrimEnd('.')}", Dim: true)));
        EmulatorOptions = options;
        ReadKeepMenu();
        ReadEmulatorActions();
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].RunFailure = _runtime.FailedMethodId == _record.Id && _runtime.FailedStep == i ? _runtime.FailedReason : string.Empty;
            Rows[i].Notify();
        }
        foreach (var name in new[]
                 {
                     nameof(EmulatorOptions), nameof(EmulatorText), nameof(EmulatorTip), nameof(AddressWatermark), nameof(IsConnected), nameof(CanOpenEmulator), nameof(CanShow), nameof(CanClose), nameof(KeepText), nameof(StateText), nameof(ChipText), nameof(ChipRunning), nameof(ChipTip), nameof(EmulatorActions), nameof(KeepOptions), nameof(KeepSwitches),
                     nameof(CanConnect), nameof(CanWork), nameof(HasSteps), nameof(OnTaught), nameof(OffTaught), nameof(CanTeach),
                 })
            OnPropertyChanged(name);
        RecordCommand.NotifyCanExecuteChanged();
        ChipCommand.NotifyCanExecuteChanged();
        TeachOnCommand.NotifyCanExecuteChanged();
        TeachOffCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The Keep running menu: the three choices, then the start-up switch.</summary>
    public IReadOnlyList<MenuRowItem> KeepOptions { get; private set; } = Array.Empty<MenuRowItem>();

    public IReadOnlyList<MenuRowItem> KeepSwitches { get; private set; } = Array.Empty<MenuRowItem>();

    private void ReadKeepMenu()
    {
        var settings = Keeper.Settings;
        MenuRowItem Choice(EmulatorKeep keep, string text, string detail) =>
            new(text, string.Empty, settings.Keep == keep, string.Empty, new RelayCommand(() => { settings.Keep = keep; Keeper.Edited(); }), null, detail);
        KeepOptions = new[]
        {
            Choice(EmulatorKeep.Always, "Always", "fastest, uses memory all the time"),
            Choice(EmulatorKeep.UntilIdle, $"Until idle {IdleMinutes} min", string.Empty),
            Choice(EmulatorKeep.AfterEachAction, "Close after each action", "frees memory, ~15 s per action"),
        };
        KeepSwitches = new[]
        {
            new MenuRowItem("Start with Windows", string.Empty, settings.StartWithWindows, "Wakes the emulator when OmniControl starts, and puts OmniControl in Windows' start-up list.",
                new RelayCommand(() => { if (Keeper.SetStartWithWindows(!settings.StartWithWindows) is { } problem) _report(problem); })),
        };
    }


    private static TagItem EaseTag(EmulatorInfo e) => new(e.Ease.ToString().ToLowerInvariant(), e.Ease == EmulatorEase.Easy ? TagTone.Good : TagTone.Caution);

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
    private async Task Show()
    {
        if (_service.Simulation.EmulatorOf(_record) is not { } emulator) return;
        await Working(() => Keeper.ShowAsync(emulator, _service.Simulation.AddressOf(_record), _report));
    }

    [RelayCommand]
    private async Task CloseNow()
    {
        if (_service.Simulation.EmulatorOf(_record) is { } emulator)
            await Working(() => Keeper.CloseNowAsync(emulator, _service.Simulation.AddressOf(_record)));
    }

    /// <summary>Runs one emulator job with the pills off; its one plain-sentence problem goes to the status line.</summary>
    private async Task Working(Func<Task<string?>> job)
    {
        if (_working) return;
        _working = true;
        Refresh();
        try
        {
            if (await job() is { } problem) _report(problem);
        }
        finally
        {
            _working = false;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (_connecting) return;
        _connecting = true;
        Refresh();
        try
        {
            var setup = _service.Simulation;
            if (await setup.ConnectAsync(setup.AddressOf(_record), setup.EmulatorOf(_record), _report, show: true) is { } problem) _report($"{problem} Details are in {DeviceService.LogFile}.");
        }
        finally
        {
            _connecting = false;
            Refresh();
            _advance();
        }
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
        OnPropertyChanged(nameof(Help));
        OnPropertyChanged(nameof(HasHelp));
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

    public void Remove(SimStepItem row)
    {
        _record.Steps.Remove(row.Step);
        Rows.Remove(row);
        Renumber();
        Save();
    }

    // ---- Right-click menu of a step ----

    /// <summary>The rows of the right-click menu; every command acts on all the selected steps (<paramref name="rows"/>), in their order.</summary>
    public IReadOnlyList<MenuRowItem> RowMenu(IReadOnlyList<SimStepItem> rows) => new[]
    {
        MenuRow("Copy", () => Copy(rows)),
        MenuRow("Cut", () => { Copy(rows); Remove(rows); }),
        new MenuRowItem("Paste below", string.Empty, false, string.Empty, new RelayCommand(() => PasteBelow(rows), () => _clipboard.Count > 0)),
        MenuRow("Duplicate", () => Duplicate(rows)),
        MenuRow("Delete", () => Remove(rows)),
    };

    private static MenuRowItem MenuRow(string text, Action run) => new(text, string.Empty, false, string.Empty, new RelayCommand(run));

    private IReadOnlyList<SimStepItem> InOrder(IReadOnlyList<SimStepItem> rows) => rows.Where(Rows.Contains).OrderBy(Rows.IndexOf).ToList();

    public void Copy(IReadOnlyList<SimStepItem> rows)
    {
        _clipboard.Clear();
        _clipboard.AddRange(InOrder(rows).Select(r => r.Step.Clone()));
    }

    public void Remove(IReadOnlyList<SimStepItem> rows)
    {
        foreach (var row in InOrder(rows))
        {
            _record.Steps.Remove(row.Step);
            Rows.Remove(row);
        }
        Renumber();
        Save();
    }

    /// <summary>The copied steps go below the last of <paramref name="rows"/>.</summary>
    public void PasteBelow(IReadOnlyList<SimStepItem> rows)
    {
        if (InOrder(rows) is not { Count: > 0 } ordered) return;
        var at = Rows.IndexOf(ordered[^1]) + 1;
        foreach (var step in _clipboard)
        {
            var copy = step.Clone();
            _record.Steps.Insert(at, copy);
            Rows.Insert(at++, MakeRow(copy));
        }
        Renumber();
        Save();
    }

    public void Duplicate(IReadOnlyList<SimStepItem> rows)
    {
        Copy(rows);
        PasteBelow(rows);
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
        var problem = await _service.TestStepAsync(_device, _record, row.Step, Rows.IndexOf(row));
        row.TestFailure = problem ?? string.Empty;
        row.Notify();
        if (problem is not null) _report($"{problem} Details are in {DeviceService.LogFile}.");
    }

    // ---- Recording and picking again ----

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task Record()
    {
        if (IsRecording) { await StopRecording(); return; }
        if (Rows.Count > 0 && !await _ask(new DialogMessage("Record again?", "The steps of this device will be replaced by the new recording.", "Start recording", RememberKey: "record.again", RememberName: "Record a device again"))) return;
        var (recorder, problem) = await _service.StartRecorderAsync(_device, _record);
        if (recorder is null) { _report(problem!); return; }
        _recorder = recorder;
        _record.Steps.Clear();
        _record.Taught = new StateModel(); // a new recording may have a new switch: on and off are taught again
        Rows.Clear();
        await recorder.NoteCurrentAppAsync();
        recorder.Recorded += OnRecorded;
        recorder.BeginRecording();
        IsRecording = true;
        _runtime.Busy = true;
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

    /// <summary>Ends the recording: the app worked in becomes the first step (never the home screen), and the device's app counts as set up.</summary>
    private async Task StopRecording()
    {
        var recorder = _recorder;
        recorder?.EndRecording();
        var app = recorder?.AppPackage ?? string.Empty;
        var appLabel = recorder is not null && app.Length > 0 ? await recorder.AppLabelAsync(app) : string.Empty;
        Release();
        IsRecording = false;
        if (Rows.Count > 0)
        {
            if (Rows[0].Step.Kind != SimStepKind.OpenApp && app.Length > 0)
            {
                var open = SimSteps.Blank(SimStepKind.OpenApp);
                open.Package = app;
                open.Label = appLabel;
                _record.Steps.Insert(0, open);
                Rows.Insert(0, MakeRow(open));
            }
            _record.SimAppReady = true;
            Renumber();
        }
        Save();
        _advance();
    }

    private async Task RePickAsync(SimStepItem row)
    {
        if (row.IsPicking) { _picking?.Cancel(); return; }
        if (IsRecording || _picking is not null) { _report("Finish the recording or the other pick first."); return; }
        var (recorder, problem) = await _service.StartRecorderAsync(_device, _record);
        if (recorder is null) { _report(problem!); return; }
        _recorder = recorder;
        _picking = new CancellationTokenSource(Timing.DeviceSimRunTimeout);
        row.IsPicking = true;
        _runtime.Busy = true;
        PickHelp = "Tap the element in the emulator. Pick again to cancel.";
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
            PickHelp = string.Empty;
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
            if (words.Length == 0) { _report("That spot shows no words. Tap the words the screen must show."); return; }
            (step.Text, step.Description, step.ResourceId, step.Label) = (words, string.Empty, string.Empty, string.Empty);
        }
        else
        {
            var tapped = SimRecorder.StepOf(touch, screen);
            (step.Text, step.Description, step.ResourceId, step.Label, step.Anchor, step.X, step.Y) = (tapped.Text, tapped.Description, tapped.ResourceId, tapped.Label, tapped.Anchor, tapped.X, tapped.Y);
        }
        row.Picked();
    }

    // ---- Teaching on and off ----

    public bool OnTaught => _record.Taught.OnSamples.Count > 0;
    public bool OffTaught => _record.Taught.OffSamples.Count > 0;

    /// <summary>The teaching stage's one line: what went wrong, or what tells on from off once both are taught; empty before.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTeachNote))] private string _teachNote = string.Empty;

    public bool HasTeachNote => TeachNote.Length > 0;

    [ObservableProperty] private bool _teachNoteIsProblem;

    public bool CanTeach => !_runtime.Busy && IsConnected;

    [RelayCommand(CanExecute = nameof(CanTeach))]
    private Task TeachOn() => Teach(true);

    [RelayCommand(CanExecute = nameof(CanTeach))]
    private Task TeachOff() => Teach(false);

    private async Task Teach(bool on)
    {
        var problem = await _service.TeachAsync(_device, _record, on);
        TeachNoteIsProblem = problem is not null;
        TeachNote = problem ?? (_record.Taught.IsTaught ? $"Learned: OmniControl tells on from off by {_record.Taught.Summary}." : string.Empty);
        Refresh();
        _advance();
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
