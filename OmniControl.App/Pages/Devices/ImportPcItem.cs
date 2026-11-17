using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Devices;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

/// <summary>One device of another PC in the import group: a tick, its icon, its name and the ways it is reached; dimmed and unticked when it is already here.</summary>
public sealed partial class ImportDeviceItem : ObservableObject
{
    private readonly Action _changed;

    public ImportDeviceItem(DeviceRecord record, bool isHere, Action changed)
    {
        Record = record;
        IsHere = isHere;
        _changed = changed;
    }

    public DeviceRecord Record { get; }

    public string Name => Record.Name.Length > 0 ? Record.Name : "A device";

    /// <summary>The icon the device chose; the plug when it chose none.</summary>
    public string Icon => Record.Icon.Length > 0 ? Record.Icon : Icons.IconLibrary.Plug;

    /// <summary>The ways it is reached, in their order ("Scripts · Simulation"), as the row's muted words.</summary>
    public string Methods => string.Join(" · ", Record.Methods.Select(m => m.Kind));

    /// <summary>A device already in this PC's list cannot be ticked.</summary>
    public bool IsHere { get; }

    public bool CanTick => !IsHere;

    public IReadOnlyList<TagItem> Tags => IsHere ? new[] { new TagItem("already here") } : Array.Empty<TagItem>();

    [ObservableProperty] private bool _isChecked;

    public void Toggle()
    {
        if (IsHere) return;
        IsChecked = !IsChecked;
        _changed();
    }
}

/// <summary>
/// One paired PC in the import group (docs/mockups/import-devices.html): its name and a tag (online, or when it was last seen) and the
/// devices it handed over: asked live while it is connected, otherwise the ones it handed over last time ("Nothing known yet" when it never did).
/// </summary>
public sealed partial class ImportPcItem : ObservableObject
{
    private readonly OtherPcsService _pcs;
    private readonly DeviceService _devices;
    private readonly Action _changed;
    private bool? _loadedOnline;
    private bool _busy;

    public ImportPcItem(string id, OtherPcsService pcs, DeviceService devices, Action changed)
    {
        Id = id;
        _pcs = pcs;
        _devices = devices;
        _changed = changed;
        Refresh();
    }

    public string Id { get; }

    public string Name => _pcs.Find(Id)?.Name ?? string.Empty;

    public ObservableCollection<ImportDeviceItem> Devices { get; } = new();

    /// <summary>What was handed over (kept for the import, which takes the ticked devices from it).</summary>
    public DeviceShare? Share { get; private set; }

    [ObservableProperty] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    /// <summary>One muted sentence beside the name: asking, nothing known, or why the PC could not be asked.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    public bool IsOnline => _pcs.IsOnline(Id);

    /// <summary>Reads the tag again, and asks the PC when it came online (or went offline) since the list was last made.</summary>
    public void Refresh()
    {
        var online = IsOnline;
        Tags = new[] { online ? new TagItem("online", TagTone.Good) : new TagItem(_pcs.SeenWords(Id)) };
        OnPropertyChanged(nameof(Name));
        if (_busy || _loadedOnline == online) return;
        _ = Guard.RunAsync("Import devices: asking a PC", () => LoadAsync(online));
    }

    /// <summary>Marks every device that is now here as such (after an import).</summary>
    public void Reload() => Show(Share, null);

    private async Task LoadAsync(bool online)
    {
        _busy = true;
        _loadedOnline = online;
        string? text = null, why = null;
        if (online)
        {
            Note = "asking…";
            var reply = await _pcs.RequestDeviceSetupsAsync(Id, includeScripts: false);
            (text, why) = (reply.Payload, reply.Why);
        }
        _busy = false;
        Show(DeviceShareFormat.Read(text ?? _pcs.KnownDeviceSetups(Id)), why);
    }

    private void Show(DeviceShare? share, string? why)
    {
        Share = share;
        Devices.Clear();
        foreach (var device in share?.Devices ?? new List<DeviceRecord>()) Devices.Add(new ImportDeviceItem(device, _devices.IsHere(device), _changed));
        Note = why ?? (share is null ? "Nothing known yet" : share.Devices.Count == 0 ? "No devices there yet" : string.Empty);
        _changed();
    }
}
