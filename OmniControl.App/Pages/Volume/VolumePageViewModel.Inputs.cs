using OmniControl.App.Audio.Engine;
using OmniControl.App.Audio;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>The Microphones list (recording devices, then combined microphones) and the rows of combined devices of either kind.</summary>
public sealed partial class VolumePageViewModel
{
    private readonly Dictionary<string, InputItem> _inputs = new();
    private readonly Dictionary<string, ComboItem> _combos = new();
    private DateTime _inputChange;

    /// <summary>The OSD element (and its "Show in OSD" switch) for the default recording device's level.</summary>
    private const string MicrophoneElement = "Microphone";

    private void RefreshInputs()
    {
        var seen = new HashSet<string>();
        var ordered = new List<VolumeRow>();
        foreach (var d in _mixer.RecordingDevices.Where(d => !VirtualCables.IsCable(d.Name)))
        {
            seen.Add(d.Id);
            if (!_inputs.TryGetValue(d.Id, out var item)) _inputs[d.Id] = item = NewInput(d);
            item.Name = d.Name;
            item.Lead = d.IsDefault ? "Default microphone" : string.Empty;
            if (!Grace.Within(_inputChange, Timing.SliderEchoGrace) && CoreAudio.GetDeviceVolume(d.Id, out var v, out var muted))
                item.Sync(v, muted, _mixer.IsFxOn(item.Key), _mixer.FxChain(item.Key).Count);
            SyncRow(item);
            ordered.Add(item);
        }
        RemoveMissing(_inputs, seen);
        ordered.AddRange(_combos.Values.Where(c => c.IsMicrophone));
        if (!ordered.SequenceEqual(Inputs.Items))
        {
            Inputs.Items.Clear();
            foreach (var row in ordered) Inputs.Items.Add(row);
        }
        UpdateSummaries();
        SyncWiring();
    }

    private InputItem NewInput(AudioDevice d)
    {
        var id = d.Id;
        var key = _mixer.InputKey(id, d.Name);
        return new InputItem(id, Gate)
        {
            Name = d.Name,
            VolumeChanged = v => { _inputChange = DateTime.UtcNow; CoreAudio.SetDeviceVolume(id, v); },
            MuteChanged = m => { _inputChange = DateTime.UtcNow; CoreAudio.SetDeviceMute(id, m); },
            FxToggled = on => _mixer.SetFxOn(key, on),
            OpenWindowRequested = () => _openFx(key),
        };
    }

    /// <summary>Brings the combined devices' rows in line with the stored ones; returns them in the stored order.</summary>
    private List<ComboItem> SyncCombos()
    {
        var seen = new HashSet<string>();
        var rows = new List<ComboItem>();
        foreach (var c in _mixer.Combined)
        {
            seen.Add(c.Key);
            if (!_combos.TryGetValue(c.Key, out var row)) _combos[c.Key] = row = NewCombo(c);
            row.Name = c.Name;
            row.Sync(c.Volume, c.Muted, c.FxOn, c.Fx.Count);
            SyncRow(row);
            rows.Add(row);
        }
        RemoveMissing(_combos, seen);
        return rows;
    }

    private ComboItem NewCombo(CombinedDevice device)
    {
        var key = device.Key;
        ComboItem? row = null;
        row = new ComboItem(device, Gate)
        {
            Name = device.Name,
            VolumeChanged = v => _mixer.SetCombinedLevel(key, v, row!.Muted),
            MuteChanged = m => _mixer.SetCombinedLevel(key, row!.Volume, m),
            FxToggled = on => _mixer.SetFxOn(key, on),
            OpenWindowRequested = () => _openFx(key),
            WiringRequested = () => ShowWiring(key),
            Forget = () => _ = ForgetComboAsync(key),
        };
        return row;
    }

    /// <summary>Removing a combined device asks first: apps that use its cable lose it.</summary>
    private async Task ForgetComboAsync(string key)
    {
        if (_mixer.CombinedOf(key) is not { } c) return;
        if (!await _ask(new EngineMessage($"Remove {c.Name}?", "Programs that play on it go back to the master; apps that use its cable keep the cable but hear nothing from it.", "Remove"))) return;
        _mixer.RemoveCombined(key);
        _report($"Removed {c.Name}");
    }
}
