using OmniControl.App.Features.Displays.Pages;

namespace OmniControl.App.Features.Displays;

/// What a monitor preset needs of one display: what it shows now and a way to show and write the values of an entry.
internal interface IMonitorPresetTarget
{
    string Id { get; }
    string Name { get; }

    /// What the display shows now (what OmniControl last read or set); null for a display with nothing a preset keeps.
    LiveMonitor? Snapshot();

    /// Takes the values and writes them to the monitor (queued, one write at a time).
    void Write(IReadOnlyList<PlannedWrite> writes);
}

/// A display as a monitor preset sees it. Presets keep every level and choice the display reports, whatever answers it (DDC/CI, or Windows' brightness for a laptop's panel). Orientation is not kept: it is not a VCP feature and turning a screen is not part of a picture setting.
internal sealed partial class HardwareDisplayModel : IMonitorPresetTarget
{
    public LiveMonitor? Snapshot()
    {
        if (_capabilities is null) return null;
        var features = new List<LiveFeature>();
        foreach (var (level, state) in _levelStates)
        {
            if (state.IsAvailable && _capabilities.Level(level.Code) is { } range)
                features.Add(new LiveFeature(level.Code, state.Value, range.Maximum, Array.Empty<uint>()));
        }
        foreach (var (choice, state) in _choiceStates)
        {
            if (!state.IsAvailable || state.Values.FirstOrDefault(v => v.Name == state.Selected) is not { } shown) continue;
            features.Add(new LiveFeature(choice.Feature.Code, (int)shown.Value, 0, state.Values.Select(v => v.Value).ToList()));
        }
        return features.Count == 0 ? null : new LiveMonitor(Id, Name, features);
    }

    public void Write(IReadOnlyList<PlannedWrite> writes)
    {
        foreach (var write in writes)
        {
            if (write.Kind == VcpKind.Level) WriteLevel(write);
            else WriteChoice(write);
        }
        Changed?.Invoke();
    }

    /// The level takes the value and the newest wanted level goes to the monitor through the shared writer.
    private void WriteLevel(PlannedWrite write)
    {
        var level = _levelStates.Keys.FirstOrDefault(l => l.Code == write.Code);
        if (level is null) return;
        _levelStates[level].Value = write.Value;
        _levels.Set(KeyOf(level), write.Value, p => Write(level, p));
    }

    private void WriteChoice(PlannedWrite write)
    {
        var choice = _choiceStates.Keys.FirstOrDefault(c => c.Feature.Code == write.Code);
        if (choice is null || _choiceStates[choice].Values.FirstOrDefault(v => v.Value == (uint)write.Value) is not { } value) return;
        _choiceStates[choice].Selected = value.Name;
        _levels.Set($"{Id}:{write.Code}:choice", write.Value, raw => Fail(_access.WriteRaw(choice.Feature.Code, (uint)raw)));
    }
}
