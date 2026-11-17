using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Devices;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// The "Import from another PC" group of the Smart Devices page (docs/mockups/import-devices.html): each paired PC with the devices it
/// handed over (<see cref="ImportPcItem"/>), the "Include scripts" option and the Import pill. What travels and what is added is
/// <see cref="DeviceService"/>'s (<see cref="DeviceService.Import"/>); how the PCs are asked is <see cref="OtherPcsService"/>'s.
/// </summary>
public sealed partial class DeviceImportViewModel : ObservableObject, IDisposable
{
    private readonly DeviceService _devices;
    private readonly OtherPcsService _pcs;
    private readonly Action<string> _report;
    private readonly Action _openOtherPcs;

    public DeviceImportViewModel(DeviceService devices, OtherPcsService pcs, Action<string> report, Action openOtherPcs)
    {
        _devices = devices;
        _pcs = pcs;
        _report = report;
        _openOtherPcs = openOtherPcs;
        Group = new CardGroup("Devices/Import", "Import from another PC", string.Empty) { SummaryWhenClosed = true };
        _pcs.Changed += Refresh;
        Refresh();
    }

    public CardGroup Group { get; }

    public ObservableCollection<ImportPcItem> Pcs { get; } = new();

    public bool HasPcs => Pcs.Count > 0;
    public bool NoPcs => Pcs.Count == 0;

    /// <summary>Also copy the user's own script templates the chosen devices use (off by default: they are commands that will run on this PC).</summary>
    [ObservableProperty] private bool _includeScripts;

    /// <summary>One thin line under the list: how the last import went, or why it did not.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string _message = string.Empty;

    [ObservableProperty] private bool _importing;

    public bool HasMessage => Message.Length > 0;

    private IEnumerable<ImportDeviceItem> Ticked => Pcs.SelectMany(p => p.Devices).Where(d => d.IsChecked);

    public int TickedCount => Ticked.Count();

    public string ImportText => TickedCount > 0 ? $"Import {TickedCount}" : "Import";

    /// <summary>The device list was imported: the page shows the new devices.</summary>
    public event Action<IReadOnlyList<DeviceRecord>>? Imported;

    /// <summary>Opens the group and asks the PCs that are online.</summary>
    public void Open()
    {
        Group.IsExpanded = true;
        Refresh();
    }

    /// <summary>Follows the paired PCs (a PC paired, unpaired, online or offline) and asks the ones that need it.</summary>
    public void Refresh()
    {
        var pairs = _pcs.Pairs;
        foreach (var gone in Pcs.Where(p => pairs.All(x => x.Id != p.Id)).ToList()) Pcs.Remove(gone);
        foreach (var pair in pairs)
        {
            if (Pcs.FirstOrDefault(p => p.Id == pair.Id) is { } known) known.Refresh();
            else Pcs.Add(new ImportPcItem(pair.Id, _pcs, _devices, Changed));
        }
        OnPropertyChanged(nameof(HasPcs));
        OnPropertyChanged(nameof(NoPcs));
        Changed();
    }

    private void Changed()
    {
        Group.Summary = Pcs.Count == 0 ? "No PC paired" : string.Join(", ", Pcs.Select(p => p.Name));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(ImportText));
        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void GoToOtherPcs() => _openOtherPcs();

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        Importing = true;
        Message = string.Empty;
        ImportCommand.NotifyCanExecuteChanged();
        try
        {
            var devices = new List<DeviceRecord>();
            var profiles = new List<ScriptProfile>();
            var skipped = new List<string>();
            foreach (var pc in Pcs.Where(p => p.Devices.Any(d => d.IsChecked)))
            {
                var share = await ShareOfAsync(pc);
                var ids = pc.Devices.Where(d => d.IsChecked).Select(d => d.Record.Id).ToHashSet();
                if (share is null) { skipped.Add(pc.Name); continue; }
                devices.AddRange(share.Devices.Where(d => ids.Contains(d.Id)));
                profiles.AddRange(share.Profiles);
            }
            var scriptsMissing = IncludeScripts && Pcs.Any(p => !p.IsOnline && p.Devices.Any(d => d.IsChecked));
            var added = _devices.Import(devices, profiles);
            foreach (var pc in Pcs) pc.Reload();
            Message = Sentence(added.Count, skipped, scriptsMissing);
            if (added.Count == 0) return;
            Imported?.Invoke(added);
            _report(Message);
        }
        finally
        {
            Importing = false;
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImport() => !Importing && TickedCount > 0;

    /// <summary>What to import from the PC: with scripts, asked again (the listing never holds them); a PC that cannot be asked gives what it handed over last time, without scripts.</summary>
    private async Task<DeviceShare?> ShareOfAsync(ImportPcItem pc)
    {
        if (!IncludeScripts || !pc.IsOnline) return pc.Share;
        var reply = await _pcs.RequestDeviceSetupsAsync(pc.Id, includeScripts: true);
        return DeviceShareFormat.Read(reply.Payload);
    }

    private static string Sentence(int added, IReadOnlyList<string> skipped, bool scriptsMissing)
    {
        var text = added == 0 ? "Nothing was imported." : added == 1 ? "Imported 1 device." : $"Imported {added} devices.";
        if (added > 0) text += " Passwords are never sent: type each device's sign-in again in its set-up.";
        if (skipped.Count > 0) text += $" {string.Join(", ", skipped)} could not be asked.";
        if (scriptsMissing) text += " Scripts need the other PC online.";
        return text;
    }

    public void Dispose() => _pcs.Changed -= Refresh;
}
