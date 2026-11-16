using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;
using OmniControl.App.Dialogs;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// One device on the Devices page: its fields (each saved as it is left), its state tag, the On and Off pills, its set-up checklist and the one
/// thin line saying how its last command went. What it is told and how is <see cref="DeviceService"/>'s; this only shows and edits it.
/// </summary>
public sealed partial class DeviceItem : ObservableObject, IDisposable
{
    private readonly DeviceService _service;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string> _report;
    private readonly Func<DeviceItem, Task> _delete;

    public DeviceRecord Record { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _address;
    [ObservableProperty] private string _email;
    [ObservableProperty] private bool _reserved;

    private readonly IReadOnlyList<SetupStepItem> _scriptSteps;
    private readonly IReadOnlyList<SetupStepItem> _simulationSteps;

    /// <summary>The simulation method's emulator, adb address and recorded steps.</summary>
    public SimulationViewModel Simulation { get; }

    /// <summary>The set-up checklist of the device's method.</summary>
    public IReadOnlyList<SetupStepItem> Checklist => IsSimulation ? _simulationSteps : _scriptSteps;

    public string Label => Record.Name;

    public bool IsSimulation => Record.Method == DeviceMethods.Simulation;
    public bool IsScripts => !IsSimulation;

    public string MethodLabel => $"Method: {Record.Method}";

    public string MethodTip => DeviceMethods.Tip(Record.Method);

    /// <summary>The Method menu: every method of the master list, a tick on the device's.</summary>
    public IReadOnlyList<MenuRowItem> MethodOptions =>
        DeviceMethods.All.Select(m => new MenuRowItem(m, string.Empty, m == Record.Method, DeviceMethods.Tip(m), new RelayCommand(() => PickMethod(m)))).ToList();

    /// <summary>The set-up checklist is shown until the device has passed its test once (with its method).</summary>
    public bool ShowsChecklist => IsSimulation ? !Record.SimVerified : !Record.Verified;

    public bool HasPassword => _service.HasPassword(Record);

    public string PasswordWatermark => HasPassword ? "Password saved in Windows (type to replace)" : "Password";

    public bool CanSwitch => _service.MissingFor(Record) is null && !_runtime.Busy;

    public string SwitchTip => _service.MissingFor(Record) is { } missing ? $"Not ready: {missing}." : "Switch the plug now.";

    public string StateText => _runtime.Busy ? "working" : _service.MissingFor(Record) is not null ? "set-up needed" : _runtime.IsOn switch { true => "on", false => "off", _ => "ready" };

    public bool StateIsCaution => !_runtime.Busy && _service.MissingFor(Record) is not null;
    public bool StateIsGood => !_runtime.Busy && _service.MissingFor(Record) is null;

    public string LastLine => _runtime.LastLine;
    public bool HasLastLine => _runtime.LastLine.Length > 0;
    public bool LastFailed => _runtime.LastFailed;

    public string RouterLinkText => _service.Setup.RouterPage is { } page ? $"Open {page}" : "Open your router's page";

    /// <summary>The password being typed; saved to Windows when the box is left, then cleared (it is never kept here).</summary>
    [ObservableProperty] private string _password = string.Empty;

    public DeviceItem(DeviceRecord record, DeviceService service, Action<string> report, Func<DialogMessage, Task<bool>> ask, Func<DeviceItem, Task> delete)
    {
        Record = record;
        _service = service;
        _runtime = service.RuntimeOf(record);
        _report = report;
        _delete = delete;
        _name = record.Name;
        _address = record.Address;
        _email = record.Email;
        _reserved = record.Reserved;
        Simulation = new SimulationViewModel(record, service, report, ask, Refresh);
        _simulationSteps = MakeSimulationChecklist();
        _scriptSteps = new[]
        {
            new SetupStepItem(() => _service.Setup.PythonPath is not null, () => _service.Setup.PythonPath is not null ? "Python found" : "Python not found")
            {
                ActionCommand = new RelayCommand(OpenPythonPage),
                ActionTip = "Open the page where Python can be downloaded. Install it, then come back.",
            },
            new SetupStepItem(() => _service.Setup.KasaInstalled, () => "python-kasa installed") { ActionCommand = InstallCommand, ActionTip = "Install python-kasa for you with pip (no administrator needed). The output goes to the devices log." },
            new SetupStepItem(() => Record.Reserved, () => "Address reserved in the router") { OpensGuide = true, ActionTip = "Steps to give the plug a fixed address." },
            new SetupStepItem(() => Record.Email.Trim().Length > 0 && HasPassword, () => "Signed in (Tapo account and password below)"),
            new SetupStepItem(() => Record.Verified, () => "Tested") { ActionCommand = TestCommand, ActionTip = "Turns the plug off, then on again." },
        };
        _service.Setup.Changed += OnOutsideChange;
        _service.Simulation.Changed += OnOutsideChange;
        _runtime.Changed += OnOutsideChange;
        Refresh();
    }

    public void Dispose()
    {
        _service.Setup.Changed -= OnOutsideChange;
        _service.Simulation.Changed -= OnOutsideChange;
        _runtime.Changed -= OnOutsideChange;
        Simulation.Dispose();
    }

    private static void RefreshChecklist(IReadOnlyList<SetupStepItem> steps)
    {
        var earlierOpen = false;
        foreach (var step in steps)
        {
            step.Refresh(earlierOpen);
            earlierOpen |= !step.Done;
        }
    }

    private void PickMethod(string method)
    {
        if (Record.Method == method) return;
        Record.Method = method;
        _service.Edited();
        if (IsSimulation) _ = _service.Simulation.RefreshAsync();
        Refresh();
    }

    // The set-up probes and the commands finish off the UI thread.
    private void OnOutsideChange() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Reads everything this item shows from the device and the set-up again.</summary>
    public void Refresh()
    {
        RefreshChecklist(_scriptSteps);
        _scriptSteps[0].ActionLabel = _service.Setup.PythonPath is null ? "Get Python" : string.Empty;
        _scriptSteps[1].ActionLabel = _service.Setup.KasaInstalled ? string.Empty : _service.Setup.Installing ? "Installing..." : "Install";
        _scriptSteps[1].ActionEnabled = _service.Setup.PythonPath is not null && !_service.Setup.Installing;
        _scriptSteps[2].ActionLabel = "Router guide";
        _scriptSteps[4].ActionLabel = Record.Verified ? string.Empty : "Test";
        _scriptSteps[4].ActionEnabled = CanSwitch;
        RefreshSimulationChecklist();
        Simulation.Refresh();
        OnCommand.NotifyCanExecuteChanged();
        OffCommand.NotifyCanExecuteChanged();
        foreach (var property in new[]
                 {
                     nameof(ShowsChecklist), nameof(Checklist), nameof(MethodLabel), nameof(MethodTip), nameof(MethodOptions), nameof(IsSimulation), nameof(IsScripts), nameof(HasPassword), nameof(PasswordWatermark), nameof(CanSwitch), nameof(SwitchTip), nameof(StateText),
                     nameof(StateIsCaution), nameof(StateIsGood), nameof(LastLine), nameof(HasLastLine), nameof(LastFailed), nameof(RouterLinkText), nameof(AddressLooksWrong),
                 })
            OnPropertyChanged(property);
    }

    partial void OnNameChanged(string value)
    {
        if (value.Trim().Length == 0) return; // a blank name is not kept
        _service.Rename(Record, value.Trim());
        OnPropertyChanged(nameof(Label));
    }

    partial void OnAddressChanged(string value)
    {
        Record.Address = value.Trim();
        _service.Edited();
        Refresh();
    }

    partial void OnEmailChanged(string value)
    {
        Record.Email = value.Trim();
        _service.Edited();
        Refresh();
    }

    partial void OnReservedChanged(bool value)
    {
        Record.Reserved = value;
        _service.Edited();
        Refresh();
    }

    partial void OnPasswordChanged(string value)
    {
        if (value.Length == 0) return;
        if (!_service.SavePassword(Record, value)) _report("Windows wouldn't keep the password. Try again.");
        Password = string.Empty; // the box empties at once; the password is only in Windows from here on
        Refresh();
    }

    /// <summary>True when the address box holds something that is not an IP address, so the view can say so.</summary>
    public bool AddressLooksWrong => Address.Trim().Length > 0 && !IPAddress.TryParse(Address.Trim(), out _);

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private Task On() => Switch(DevicePower.On);

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private Task Off() => Switch(DevicePower.Off);

    private async Task Switch(DevicePower power) => await _service.RunAsync(Record, power);

    [RelayCommand]
    private async Task Test()
    {
        if (await _service.TestAsync(Record) is null) Refresh();
    }

    [RelayCommand]
    private async Task Install()
    {
        if (await _service.Setup.InstallAsync() is { } problem) _report($"{problem} Details are in {DeviceService.LogFile}.");
    }

    [RelayCommand]
    private Task Delete() => _delete(this);

    [RelayCommand]
    private void OpenRouter()
    {
        if (_service.Setup.OpenRouterPage() is { } problem) _report(problem);
    }

    private void OpenPythonPage()
    {
        if (_service.Setup.OpenPythonPage() is { } problem) _report(problem);
    }
}
