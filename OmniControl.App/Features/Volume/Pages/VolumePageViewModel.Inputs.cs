using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Volume.Pages;

/// The Microphones list (recording devices, then combined microphones) and the rows of combined devices of either kind.
public sealed partial class VolumePageViewModel
{
    private readonly Dictionary<string, InputItem> _inputs = new();
    private readonly Dictionary<string, ComboItem> _combos = new();

    /// The word under the default recording device's name.
    private const string DefaultWord = "default";

    private const string MicrophoneElement = OsdService.MicrophoneElement;

    /// The own glyphs (Styles/Brushes.axaml) of the microphone and effects actions and states.
    private const string MicIcon = Icons.IconLibrary.Mic, FxIcon = Icons.IconLibrary.Sliders;

    private void RefreshInputs()
    {
        var seen = new HashSet<string>();
        var ordered = new List<VolumeRow>();
        foreach (var d in _mixer.RecordingDevices.Where(d => !VirtualCables.IsCable(d.Name)))
        {
            seen.Add(d.Id);
            if (!_inputs.TryGetValue(d.Id, out var item)) _inputs[d.Id] = item = NewInput(d);
            item.Name = d.Name;
            item.StateWord = d.IsDefault ? DefaultWord : string.Empty;
            if (!_actions.InputJustChanged && CoreAudio.GetDeviceVolume(d.Id, out var v, out var muted))
                item.Sync(v, muted, _mixer.IsFxOn(item.Key), FxSlot.EffectCount(_mixer.FxChain(item.Key)));
            SyncListen(item);
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
        InputItem? row = null;
        return row = new InputItem(id)
        {
            ListenToggled = on => SetListen(row!, on, null, keepOutput: true),
            ListenMenu = () => ListenRows(row!),
            Name = d.Name,
            VolumeChanged = v => { _actions.NoteInputChange(); CoreAudio.SetDeviceVolume(id, v); },
            MuteChanged = m => { _actions.NoteInputChange(); CoreAudio.SetDeviceMute(id, m); },
            FxToggled = on => _mixer.SetFxByUser(key, on),
            FxWindowToggled = () => _toggleFx(key),
            RouteChoice = RouteChoice.ForMicrophone(route => { _mixer.SetMicMethod(key, route); SyncRow(row!); }, () => SyncRow(row!)),
        };
    }

    /// Brings the combined devices' rows in line with the stored ones; returns them in the stored order.
    private List<ComboItem> SyncCombos()
    {
        var seen = new HashSet<string>();
        var rows = new List<ComboItem>();
        foreach (var c in _mixer.Combined)
        {
            seen.Add(c.Key);
            if (!_combos.TryGetValue(c.Key, out var row)) _combos[c.Key] = row = NewCombo(c);
            row.Name = c.Name;
            row.Sync(c.Volume, c.Muted, c.FxOn, FxSlot.EffectCount(c.Fx));
            SyncRow(row);
            rows.Add(row);
        }
        RemoveMissing(_combos, seen);
        SyncHotkeyAvailability();
        return rows;
    }

    private ComboItem NewCombo(CombinedDevice device)
    {
        var key = device.Key;
        ComboItem? row = null;
        row = new ComboItem(device)
        {
            Name = device.Name,
            VolumeChanged = v => _mixer.SetCombinedLevel(key, v, row!.Muted),
            MuteChanged = m => _mixer.SetCombinedLevel(key, row!.Volume, m),
            FxToggled = on => _mixer.SetFxByUser(key, on),
            FxWindowToggled = () => _toggleFx(key),
            WiringRequested = () => ShowWiring(key),
            Forget = () => _ = ForgetComboAsync(key),
            RouteChoice = device.Kind == CombinedKind.Microphones ? RouteChoice.ForCombinedMicrophone(() => SyncRow(row!)) : null,
        };
        return row;
    }

    /// Removing a combined device asks first: apps that use its cable lose it.
    private async Task ForgetComboAsync(string key)
    {
        if (_mixer.CombinedOf(key) is not { } c) return;
        var line = c.Kind == CombinedKind.Microphones
            ? $"Apps using {RecordingNameFor(c) ?? "its cable"} hear nothing."
            : "Programs on it go back to the master; apps using its cable hear nothing.";
        if (!await _ask(new DialogMessage($"Remove {c.Name}?", line, "Remove", RememberKey: "remove.combined", RememberName: "Remove a combined device"))) return;
        _mixer.RemoveCombined(key);
        _report($"Removed {c.Name}");
    }
}
