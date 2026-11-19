using OmniControl.App.Features.Devices;

namespace OmniControl.App.Features.Devices.Pages;

// Setup mode: a new device, a method row or its
// "Edit setup…", "+ add method" or Re-teach shows that one method's numbered stages (MethodItem). Cancel puts the device back as it was when
// the set-up opened (a device that was never ready is removed, after asking); Finish setup, once every stage is done, makes the method
// ready. While the set-up is open the simulation's emulator shows its window.
public sealed partial class DeviceItem
{
    private DeviceRecord? _before;

    /// The set-up is shown instead of the Ready view.
    [ObservableProperty] private bool _inSetup;

    /// The method whose set-up is open; null in the Ready view.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsTemplateChip), nameof(RemoveTip))] private MethodItem? _editing;

    public bool CanFinish => Editing?.CanFinish == true;

    /// The header's "ready" tag, state tag, Check now and Auto-check show in the Ready view only; the Template chip in a scripts method's set-up only.
    public bool ShowsReadyTags => IsReady && !InSetup;
    public bool ShowsStateTag => HasStateTag && !InSetup;
    public bool ShowsTemplateChip => InSetup && Editing?.IsScripts == true;

    /// The header's cross: in a set-up it removes that method (the device when it is the only one); in the Ready view it deletes the device.
    public string RemoveTip => InSetup && Record.Methods.Count > 1 ? "Remove this method" : "Delete this device";

    partial void OnInSetupChanged(bool value)
    {
        _runtime.InSetup = value;
        foreach (var property in new[] { nameof(ShowsReadyTags), nameof(ShowsStateTag), nameof(ShowsTemplateChip), nameof(RemoveTip) }) OnPropertyChanged(property);
    }

    /// Opens a method's set-up; the device is copied first so Cancel can put it back.
    private void EditSetup(MethodItem method)
    {
        if (InSetup && Editing == method) return;
        _before ??= Record.Clone();
        OpenSetup(method);
        Refresh();
    }

    private void OpenSetup(MethodItem method)
    {
        Editing = method;
        method.BeginSetup();
        InSetup = true;
    }

    /// Opens the drifting method's set-up at its teaching stage (the Ready view's Re-teach).
    [RelayCommand]
    private void ReTeach()
    {
        if (Drifting is not { } method) return;
        EditSetup(method);
        method.OpenTeachStage();
    }

    /// "+ add method": a method of that kind goes last in the order and its set-up opens (Cancel takes it away again).
    private void AddMethod(string kind)
    {
        _before ??= Record.Clone();
        var record = _service.AddMethod(Record, kind);
        SyncMethods();
        if (kind == DeviceMethods.Simulation) _ = _service.Simulation.RefreshAsync();
        EditSetup(Methods.First(m => m.Record == record));
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (_before is not { } before)
        {
            await _delete(this); // never set up: cancelling is not adding it (the delete asks first)
            return;
        }
        _service.Restore(Record, before);
        _before = null;
        Name = Record.Name;
        CloseSetup();
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish()
    {
        if (Editing is { } method) _service.FinishSetup(method.Record);
        _before = null;
        CloseSetup();
    }

    /// The header's cross in a set-up: removes the method being set up (asks first), or deletes the device when it is its only method.
    [RelayCommand]
    private async Task Remove()
    {
        if (!InSetup || Editing is not { } method || Record.Methods.Count < 2)
        {
            await _delete(this);
            return;
        }
        if (!await _ask(Confirm.Delete("method", Record.NameOf(method.Record)))) return;
        _service.RemoveMethod(Record, method.Record);
        _before = null;
        CloseSetup();
    }

    private void CloseSetup()
    {
        if (Editing is { Record.IsSimulation: true } simulated) _ = _service.Simulation.HideWindowAsync(simulated.Record);
        Editing = null;
        InSetup = false;
        SyncMethods();
        Refresh();
    }
}
