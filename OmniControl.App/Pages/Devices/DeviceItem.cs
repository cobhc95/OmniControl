using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;
using OmniControl.App.Dialogs;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// One device on the Smart Devices page (docs/mockups/smart-devices-setup-vs-ready.html, device-icons.html, device-multiple-methods.html):
/// its list row (icon, name, a switch showing the last known state, or "set-up"), the Ready view (name, state, On / Off / Toggle, its
/// methods in the order they are tried, the last command) and, while a method's set-up is open, that method's stages
/// (<c>DeviceItem.Setup.cs</c>, <see cref="MethodItem"/>). The icon is <c>DeviceItem.Icon.cs</c>, the state reading <c>DeviceItem.Check.cs</c>.
/// What the device is told and how is <see cref="DeviceService"/>'s; this only shows and edits it.
/// </summary>
public sealed partial class DeviceItem : ObservableObject, IDisposable
{
    private readonly DeviceService _service;
    private readonly DeviceRuntime _runtime;
    private readonly Action<string> _report;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<DeviceItem, Task> _delete;
    private readonly Func<Avalonia.Visual, Icons.IconRequest, Task<Icons.IconPick?>> _chooseIcon;

    public DeviceRecord Record { get; }

    /// <summary>The device's name, shared by the name box and the list: every key renames it at once (and its actions); the file is saved shortly after.</summary>
    [ObservableProperty] private string _name;

    /// <summary>The device's methods, in the order they are tried.</summary>
    public ObservableCollection<MethodItem> Methods { get; } = new();

    /// <summary>The "+ add method" menu: every built method of the master list.</summary>
    public IReadOnlyList<MenuRowItem> AddMethodOptions =>
        DeviceMethods.All.Select(m => new MenuRowItem(m.Name, string.Empty, false, m.Tip, new RelayCommand(() => AddMethod(m.Name)), m.Tags, m.Brief)).ToList();

    public bool IsReady => Record.IsReady;

    public bool CanCheck => _service.MissingFor(Record) is null && !_runtime.Busy;

    public bool CanSwitch => _service.MissingFor(Record) is null && !_runtime.Busy;

    public string SwitchTip => _service.MissingFor(Record) is { } missing ? $"Not ready: {missing}." : "Switch it now (the methods are tried in order).";

    /// <summary>The list row's switch: the last known state; turning it switches the device (the switch then follows what was read).</summary>
    public bool ListOn
    {
        get => _runtime.IsOn == true;
        set
        {
            if (value == ListOn || !CanSwitch) { OnPropertyChanged(); return; }
            _ = Switch(value ? DevicePower.On : DevicePower.Off);
        }
    }

    /// <summary>The state tag: "on · read 14:52" (read back from the device), "on · set 14:52" (only told), "unknown · read ...", "working…" while busy; empty when nothing is known yet.</summary>
    public string StateTagText
    {
        get
        {
            if (_runtime.Busy) return _runtime.Checking ? "checking…" : "working…";
            if (_runtime.KnownAt is not { } at) return string.Empty;
            var state = _runtime.IsOn switch { true => "on", false => "off", _ => "unknown" };
            return $"{state} · {(_runtime.Confirmed || _runtime.Unsure ? "read" : "set")} {at:HH:mm}";
        }
    }

    public bool HasStateTag => StateTagText.Length > 0;
    public bool StateTagGood => !_runtime.Busy && _runtime.IsOn == true;
    public bool StateTagCaution => !_runtime.Busy && _runtime.KnownAt is not null && _runtime.IsOn is null;

    /// <summary>A simulation method whose recent readings were unsure: its sentence and Re-teach.</summary>
    private MethodItem? Drifting => Methods.FirstOrDefault(m => m.HasDrift);
    public bool HasDrift => Drifting is not null;
    public string DriftText => Drifting is { } method ? (Methods.Count > 1 ? $"{Record.NameOf(method.Record)}: {method.DriftText}" : method.DriftText) : string.Empty;

    public string LastLine => _runtime.LastLine;
    public bool HasLastLine => _runtime.LastLine.Length > 0;
    public bool LastFailed => _runtime.LastFailed;
    public string Details => _runtime.Details;
    public bool HasDetails => _runtime.LastFailed && _runtime.Details.Length > 0;

    public DeviceItem(DeviceRecord record, DeviceService service, Action<string> report, Func<DialogMessage, Task<bool>> ask, Func<DeviceItem, Task> delete,
        Func<Avalonia.Visual, Icons.IconRequest, Task<Icons.IconPick?>> chooseIcon)
    {
        Record = record;
        _service = service;
        _runtime = service.RuntimeOf(record);
        _report = report;
        _ask = ask;
        _delete = delete;
        _chooseIcon = chooseIcon;
        _name = record.Name;
        SyncMethods();
        if (!record.IsReady) OpenSetup(Methods.FirstOrDefault(m => !m.IsReady) ?? Methods[0]);
        _service.Setup.Changed += OnOutsideChange;
        _service.Simulation.Changed += OnOutsideChange;
        _runtime.Changed += OnOutsideChange;
        _service.AutoCheckChanged += OnAutoCheckChanged;
        Refresh();
    }

    public void Dispose()
    {
        _service.AutoCheckChanged -= OnAutoCheckChanged;
        _service.Setup.Changed -= OnOutsideChange;
        _service.Simulation.Changed -= OnOutsideChange;
        _runtime.Changed -= OnOutsideChange;
        _runtime.InSetup = false;
        foreach (var method in Methods) method.Dispose();
        IconBitmap?.Dispose();
    }

    /// <summary>Makes the method rows follow the record's methods (their order, added, removed), keeping the rows that stay.</summary>
    private void SyncMethods()
    {
        var kept = Methods.ToDictionary(m => m.Record);
        foreach (var gone in Methods.Where(m => !Record.Methods.Contains(m.Record)).ToList()) gone.Dispose();
        Methods.Clear();
        foreach (var record in Record.Methods)
            Methods.Add(kept.TryGetValue(record, out var item) ? item : new MethodItem(Record, record, _service, _report, _ask, Refresh, EditSetup));
        for (var i = 0; i < Methods.Count; i++) Methods[i].Number = i + 1;
        if (Editing is { } editing && !Methods.Contains(editing)) Editing = null;
    }

    /// <summary>A drag in the method list: the order On, Off, Toggle and Check try them changes.</summary>
    public void MoveMethods(IReadOnlyList<int> moved, int insertAt)
    {
        _service.MoveMethods(Record, moved, insertAt);
        SyncMethods();
        Refresh();
    }

    /// <summary>Other devices were merged into this one (or the list was restored): the method rows are read again.</summary>
    public void Reload()
    {
        SyncMethods();
        Refresh();
    }

    // The set-up probes and the commands finish off the UI thread.
    private void OnOutsideChange() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Reads everything this item shows from the device and the set-up again.</summary>
    public void Refresh()
    {
        foreach (var method in Methods) method.Refresh();
        RefreshIcon();
        FinishCommand.NotifyCanExecuteChanged();
        CheckNowCommand.NotifyCanExecuteChanged();
        foreach (var property in new[]
                 {
                     nameof(IsReady), nameof(CanSwitch), nameof(CanCheck), nameof(SwitchTip), nameof(AutoCheckLabel), nameof(AutoCheckTip), nameof(AutoCheckEditable),
                     nameof(ListOn), nameof(StateTagText), nameof(HasStateTag), nameof(StateTagGood), nameof(StateTagCaution), nameof(HasDrift), nameof(DriftText),
                     nameof(LastLine), nameof(HasLastLine), nameof(LastFailed), nameof(Details), nameof(HasDetails), nameof(CanFinish), nameof(ShowsReadyTags), nameof(ShowsStateTag),
                     nameof(ShowsTemplateChip), nameof(RemoveTip),
                 })
            OnPropertyChanged(property);
    }

    partial void OnNameChanged(string value)
    {
        if (value.Trim().Length == 0) return; // a blank name is not kept
        _service.Rename(Record, value.Trim());
    }

    private async Task Switch(DevicePower power)
    {
        await _service.RunAsync(Record, power);
        Refresh();
    }
}
