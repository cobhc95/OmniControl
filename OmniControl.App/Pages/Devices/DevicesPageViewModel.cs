using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Devices;
using OmniControl.App.Dialogs;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// The Devices page (docs/mockups/devices-tab.html): the smart plugs on the left, the chosen one on the right with its fields and, until
/// it has passed its test, the set-up checklist. The devices, their actions and their commands are <see cref="DeviceService"/>'s.
/// </summary>
public sealed partial class DevicesPageViewModel : PageViewModel, IDisposable
{
    private readonly DeviceService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _report;

    public override string Title => DeviceService.Category;

    public override string? NavGroup => NavGroups.Devices;

    public override string Icon => "M8,2 H10 V7 H8 Z M14,2 H16 V7 H14 Z M6,7 H18 V13 A6,6 0 0 1 13,18.9 V22 H11 V18.9 A6,6 0 0 1 6,13 Z M8,9 V13 A4,4 0 0 0 16,13 V9 Z";

    public override string Keywords => "devices smart plug socket outlet tapo tp-link p100 p110 kasa python-kasa switch power on off lamp router ip address simulation emulator android adb bluestacks ldplayer app steps record";

    public ObservableCollection<DeviceItem> Items { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelected))] private DeviceItem? _selected;

    public bool NoDevices => Items.Count == 0;
    public bool HasSelected => Selected is not null;

    public DevicesPageViewModel(DeviceService service, Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        _service = service;
        _ask = ask;
        _report = report;
        foreach (var record in service.Settings.Devices) Items.Add(Make(record));
        Selected = Items.FirstOrDefault();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoDevices));
    }

    // The checks of Python, python-kasa, adb and the emulators run when the page is shown, not at start-up.
    public override void OnShown()
    {
        _ = _service.Setup.RefreshAsync();
        _ = _service.Simulation.RefreshAsync();
    }

    private DeviceItem Make(DeviceRecord record) => new(record, _service, _report, _ask, DeleteAsync);

    [RelayCommand]
    private void AddDevice()
    {
        var item = Make(_service.Add());
        Items.Add(item);
        Selected = item;
    }

    private async Task DeleteAsync(DeviceItem item)
    {
        if (!await _ask(Confirm.Delete("device", item.Record.Name))) return;
        var at = Items.IndexOf(item);
        _service.Remove(item.Record);
        Items.Remove(item);
        item.Dispose();
        Selected = Items.Count == 0 ? null : Items[Math.Min(at, Items.Count - 1)];
    }

    public void Dispose()
    {
        foreach (var item in Items) item.Dispose();
    }
}
