using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Combining devices: the one path for the Wiring tab's "+ Combine devices" and the search card, and Edit on a combined device's card
/// (the same window); the steps that get a virtual cable.
/// </summary>
public sealed partial class VolumePageViewModel
{
    [RelayCommand]
    private Task Combine() => CombineAsync();

    private async Task CombineAsync()
    {
        if (!CombineGate.IsAvailable)
        {
            _report(CombineGate.Reason!);
            return;
        }
        var free = FreeCables().FirstOrDefault();
        var dialog = NewCombineDialog(free is not null ? $"{free.Name} is ready for it." : $"{CableMissing()}: the new device waits until a cable is free.",
            free is null ? CableStepFor(CableMissing()) : null, null);
        if (!await _combine(dialog)) return;
        var device = dialog.Result();
        _mixer.AddCombined(device);
        _report($"{device.Name} created");
    }

    /// <summary>Edit on a combined device's card: its name and members in the Combine devices window.</summary>
    private async Task EditComboAsync(string key)
    {
        if (_mixer.CombinedOf(key) is not { } c) return;
        var cable = CableStateOf(c.Key, c.Cable);
        var dialog = NewCombineDialog(cable.IsCaution ? $"{cable.Text}." : cable.HasTag ? $"Uses {cable.Text}." : "No virtual cable yet.",
            CableStepFor(cable.IsCaution ? cable.Text : null), c);
        if (!await _combine(dialog)) return;
        var edited = dialog.Result();
        _mixer.EditCombined(key, edited.Name, edited.Members);
        _report($"{edited.Name} saved");
    }

    private CombineDeviceViewModel NewCombineDialog(string cableStatus, ModeCardAction? cableSetup, CombinedDevice? editing) => new(
        _mixer.Devices.Where(d => !VirtualCables.IsCable(d.Name)).ToList(),
        _mixer.RecordingDevices.Where(d => !VirtualCables.IsCable(d.Name)).ToList(),
        cableStatus, cableSetup, editing);

    /// <summary>The cables no run and no combined device uses now.</summary>
    private IEnumerable<VirtualCable> FreeCables()
    {
        var busy = _engine.CablesInUse.Concat(_mixer.Combined.Select(c => c.Cable)).ToHashSet();
        return (_modes.Current?.Cables ?? Array.Empty<VirtualCable>()).Where(c => !busy.Contains(c.PlaybackId));
    }

    /// <summary>What a new cable user would wait for: VB-CABLE itself, or one more cable.</summary>
    private string CableMissing() => _modes.Current?.Cables.Count > 0 ? SoundModes.NeedsAnotherCable : SoundModes.NeedsCable;

    /// <summary>The step that gets what <paramref name="missing"/> says is missing (install VB-CABLE, or more cables); null = nothing missing.</summary>
    private ModeCardAction? CableStepFor(string? missing) => missing switch
    {
        SoundModes.NeedsCable => CableSetupAction(),
        SoundModes.NeedsAnotherCable => MoreCablesAction(),
        _ => null,
    };

    /// <summary>Install VB-CABLE: its own installer when it was placed next to OmniControl, else its download page.</summary>
    private ModeCardAction CableSetupAction() =>
        new("Install VB-CABLE", "Runs VB-CABLE's own installer if it was placed next to OmniControl, else opens its download page",
            new AsyncRelayCommand(() => RunSetupAsync(SoundModes.Full, null, _installer.InstallCableAsync())), Main: true);

    /// <summary>More cables: VB-Audio's page (each combined device, processed microphone or program needs its own cable).</summary>
    private ModeCardAction MoreCablesAction() =>
        new("More cables", "Opens VB-Audio's page: each combined device, processed microphone or program needs its own cable",
            new RelayCommand(() => _report(EngineInstaller.OpenCablePage().Text)));
}
