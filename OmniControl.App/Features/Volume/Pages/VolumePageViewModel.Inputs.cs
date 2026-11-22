using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// The Microphones list (recording devices, then combined microphones) and the rows of combined devices of either kind.
public sealed partial class VolumePageViewModel
{
    private readonly Dictionary<string, InputItem> _inputs = new();
    private readonly Dictionary<string, ComboItem> _combos = new();

    /// The word under the default recording device's name.
    private const string DefaultWord = "default";

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
            if (!_controls.InputJustChanged && _mixer.InputLevel(d.Id, out var v, out var muted))
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
            ListenToggled = on => _controls.SetListen(id, row!.Name, on, null, keepOutput: true),
            ListenMenu = () => _controls.ListenRows(id, row!.Name),
            Name = d.Name,
            VolumeChanged = v => _controls.SetInputVolume(id, v),
            MuteChanged = m => _controls.SetInputMute(id, m),
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
        return rows;
    }

    /// A level action or Windows' own report changed a microphone or a combined microphone: its row shows it.
    private void OnInputShown(string deviceId, int level, bool muted)
    {
        if (_inputs.TryGetValue(deviceId, out var row)) row.Sync(level, muted, row.FxOn, row.FxCount);
    }

    private void OnComboShown(string key, int level, bool muted)
    {
        if (_combos.TryGetValue(key, out var row)) row.Sync(level, muted, row.FxOn, row.FxCount);
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
